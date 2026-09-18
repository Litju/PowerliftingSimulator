#!/usr/bin/env python3
"""Fit the smallest honest GAM-13 local ARX model from Unity evidence."""

import csv
import json
import math
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MEASUREMENTS = ROOT / "Artifacts" / "Measurements" / "GAM-13"
MODEL_VERSION = "GAM13_DYNAMIC_PLANT_MODEL_V1"
STATE_FIELDS = (
    "com_ap_m",
    "com_velocity_ap_mps",
    "cop_ap_m",
    "capture_ap_m",
    "trunk_pitch_rad",
)
INPUT_FIELDS = (
    "applied_ankle_delta_rad",
    "applied_hip_delta_rad",
    "applied_trunk_delta_rad",
)


def finite(value):
    try:
        return math.isfinite(float(value))
    except (TypeError, ValueError):
        return False


def read_rows(name):
    with (MEASUREMENTS / name).open(newline="") as handle:
        return list(csv.DictReader(handle))


def trajectory_key(row):
    return (float(row["load_kg"]), row["split"], int(row["repeat"]))


def valid_row(row):
    return row["valid_local"].lower() == "true" and all(
        finite(row[field]) for field in STATE_FIELDS + INPUT_FIELDS
    )


def contiguous(rows):
    rows = sorted(rows, key=lambda row: int(row["tick"]))
    output = []
    for row in rows:
        if not valid_row(row):
            break
        if output and int(row["tick"]) != int(output[-1]["tick"]) + 1:
            break
        output.append(row)
    return output


def solve(matrix, rhs):
    n = len(rhs)
    augmented = [list(matrix[index]) + [rhs[index]] for index in range(n)]
    for column in range(n):
        pivot = max(range(column, n), key=lambda row: abs(augmented[row][column]))
        if abs(augmented[pivot][column]) < 1e-12:
            raise ValueError("singular normal matrix")
        augmented[column], augmented[pivot] = augmented[pivot], augmented[column]
        divisor = augmented[column][column]
        augmented[column] = [value / divisor for value in augmented[column]]
        for row in range(n):
            if row == column:
                continue
            scale = augmented[row][column]
            if scale == 0:
                continue
            augmented[row] = [
                left - scale * right
                for left, right in zip(augmented[row], augmented[column])
            ]
    return [augmented[index][-1] for index in range(n)]


def fit(rows):
    feature_count = 1 + len(STATE_FIELDS) + len(INPUT_FIELDS)
    normal = [[0.0] * feature_count for _ in range(feature_count)]
    targets = [[0.0] * feature_count for _ in STATE_FIELDS]
    pairs = 0
    for trajectory in rows:
        for current, following in zip(trajectory, trajectory[1:]):
            if int(following["tick"]) != int(current["tick"]) + 1:
                continue
            features = [1.0]
            features.extend(float(current[field]) for field in STATE_FIELDS)
            features.extend(float(current[field]) for field in INPUT_FIELDS)
            for left in range(feature_count):
                for right in range(feature_count):
                    normal[left][right] += features[left] * features[right]
            for state_index, field in enumerate(STATE_FIELDS):
                target = float(following[field])
                for column, value in enumerate(features):
                    targets[state_index][column] += target * value
            pairs += 1
    if pairs < feature_count * 2:
        return None

    coefficients = []
    for state_index in range(len(STATE_FIELDS)):
        regularized = [row[:] for row in normal]
        for diagonal in range(1, feature_count):
            regularized[diagonal][diagonal] += 1e-9
        coefficients.append(solve(regularized, targets[state_index]))
    return coefficients, pairs


def predict(coefficients, current):
    features = [1.0]
    features.extend(current)
    features.extend([0.0] * len(INPUT_FIELDS))
    return [sum(row[column] * features[column] for column in range(len(features))) for row in coefficients]


def predict_pair(coefficients, current, inputs):
    features = [1.0]
    features.extend(current)
    features.extend(inputs)
    return [sum(row[column] * features[column] for column in range(len(features))) for row in coefficients]


def rmse(values):
    return math.sqrt(sum(value * value for value in values) / len(values)) if values else None


def validation_error(coefficients, trajectories):
    one_step = []
    multi_step = []
    for trajectory in trajectories:
        if len(trajectory) < 2:
            continue
        current = [float(row[field]) for field in STATE_FIELDS for row in [trajectory[0]]]
        rollout = current[:]
        for index, row in enumerate(trajectory[:-1]):
            actual = [float(trajectory[index + 1][field]) for field in STATE_FIELDS]
            inputs = [float(row[field]) for field in INPUT_FIELDS]
            one = predict_pair(coefficients, current, inputs)
            one_step.extend(predicted - observed for predicted, observed in zip(one, actual))
            if index < min(50, len(trajectory) - 1):
                future = predict_pair(coefficients, rollout, inputs)
                multi_step.extend(predicted - observed for predicted, observed in zip(future, actual))
                rollout = future
            current = actual
    return rmse(one_step), rmse(multi_step)


def matrix_rank(matrix, tolerance=1e-7):
    if not matrix:
        return 0
    work = [row[:] for row in matrix]
    rows = len(work)
    columns = len(work[0])
    rank = 0
    for column in range(columns):
        pivot = max(range(rank, rows), key=lambda row: abs(work[row][column]))
        if abs(work[pivot][column]) <= tolerance:
            continue
        work[rank], work[pivot] = work[pivot], work[rank]
        divisor = work[rank][column]
        work[rank] = [value / divisor for value in work[rank]]
        for row in range(rows):
            if row == rank:
                continue
            scale = work[row][column]
            work[row] = [left - scale * right for left, right in zip(work[row], work[rank])]
        rank += 1
        if rank == rows:
            break
    return rank


def state_matrices(coefficients):
    state_count = len(STATE_FIELDS)
    input_count = len(INPUT_FIELDS)
    intercept = [row[0] for row in coefficients]
    a = [row[1 : state_count + 1] for row in coefficients]
    b = [row[state_count + 1 : state_count + 1 + input_count] for row in coefficients]
    return intercept, a, b


def matrix_multiply(left, right):
    return [
        [sum(left[row][inner] * right[inner][column] for inner in range(len(right))) for column in range(len(right[0]))]
        for row in range(len(left))
    ]


def controllability_rank(a, b):
    blocks = []
    power = [row[:] for row in b]
    for _ in range(len(a)):
        blocks.append(power)
        power = matrix_multiply(a, power)
    controllability = [
        [blocks[power_index][row][column]
         for power_index in range(len(blocks))
         for column in range(len(b[0]))]
        for row in range(len(a))
    ]
    return matrix_rank(controllability)


def summarize(rows_by_load, validation_by_load):
    models = []
    for load in sorted(set(rows_by_load) | set(validation_by_load)):
        id_trajectories = [contiguous(rows) for rows in rows_by_load.get(load, [])]
        val_trajectories = [contiguous(rows) for rows in validation_by_load.get(load, [])]
        id_rows = sum(len(trajectory) for trajectory in id_trajectories)
        valid_rows = sum(len(trajectory) for trajectory in val_trajectories)
        entry = {
            "load_kg": load,
            "model_version": MODEL_VERSION,
            "valid_identification_rows": id_rows,
            "valid_validation_rows": valid_rows,
            "validity": "LOCAL_EQUILIBRIUM" if id_rows >= 100 else "TRANSIENT_LOCAL_RESPONSE",
            "model_status": "INSUFFICIENT_VALID_LOCAL_WINDOW",
            "one_step_rmse": None,
            "multi_step_rmse_50_ticks": None,
            "controllability_rank": None,
            "state_dimension": len(STATE_FIELDS),
            "observability_rank": None,
            "spectral_radius_bound": None,
            "stabilizability": "NOT_ESTABLISHED",
        }
        fitted = fit(id_trajectories)
        if fitted is not None:
            coefficients, pairs = fitted
            _, a, b = state_matrices(coefficients)
            one_step, multi_step = validation_error(coefficients, val_trajectories)
            entry.update(
                model_status="FIT_LOCAL_ARX",
                one_step_rmse=one_step,
                multi_step_rmse_50_ticks=multi_step,
                controllability_rank=controllability_rank(a, b),
                observability_rank=len(STATE_FIELDS),
                spectral_radius_bound=max(sum(abs(value) for value in row) for row in a),
                stabilizability="LOCAL_ONLY_NOT_ROBUST",
                pairs=pairs,
                intercept=state_matrices(coefficients)[0],
                A=a,
                B=b,
            )
        models.append(entry)
    return models


def write_outputs(models, all_rows):
    with (MEASUREMENTS / "dynamic-plant-models.json").open("w") as handle:
        json.dump(
            {
                "model_version": MODEL_VERSION,
                "claim_class": "GAME_ENGINE_CONTROL_PLANT_IDENTIFICATION",
                "plant_version": "GAM13_PRODUCTION_PLANT_V1",
                "state": list(STATE_FIELDS),
                "inputs": list(INPUT_FIELDS),
                "models": models,
                "claim_ceiling": "No cross-load controllability, stabilizability, or robust stability claim is made without a valid local model.",
            },
            handle,
            indent=2,
        )
    with (MEASUREMENTS / "dynamic-plant-summary.csv").open("w", newline="") as handle:
        fields = [
            "load_kg", "model_version", "valid_identification_rows", "valid_validation_rows", "validity",
            "model_status", "one_step_rmse", "multi_step_rmse_50_ticks", "controllability_rank",
            "state_dimension", "observability_rank", "spectral_radius_bound", "stabilizability",
        ]
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for model in models:
            writer.writerow({field: model.get(field) for field in fields})
    grouped = defaultdict(list)
    for row in all_rows:
        grouped[float(row["load_kg"])].append(row)
    with (MEASUREMENTS / "load-transition-map.csv").open("w", newline="") as handle:
        fields = ["load_kg", "rows", "valid_rows", "valid_fraction", "first_invalid_tick", "classification"]
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for load in sorted(grouped):
            rows = sorted(grouped[load], key=lambda row: int(row["tick"]))
            valid = [row for row in rows if row["valid_local"].lower() == "true"]
            invalid = [row for row in rows if row["valid_local"].lower() != "true"]
            writer.writerow(
                {
                    "load_kg": load,
                    "rows": len(rows),
                    "valid_rows": len(valid),
                    "valid_fraction": len(valid) / len(rows),
                    "first_invalid_tick": invalid[0]["tick"] if invalid else "",
                    "classification": "LOCAL_EQUILIBRIUM" if valid else "TRANSIENT_LOCAL_RESPONSE",
                }
            )


def main():
    identification = read_rows("dynamic-id-excitation.csv")
    validation = read_rows("dynamic-id-validation.csv")
    by_load_id = defaultdict(list)
    by_load_validation = defaultdict(list)
    for key, rows in group_rows(identification).items():
        by_load_id[key[0]].append(rows)
    for key, rows in group_rows(validation).items():
        by_load_validation[key[0]].append(rows)
    models = summarize(by_load_id, by_load_validation)
    write_outputs(models, identification + validation)
    print(json.dumps(models, indent=2))


def group_rows(rows):
    groups = defaultdict(list)
    for row in rows:
        groups[trajectory_key(row)].append(row)
    return groups


if __name__ == "__main__":
    main()
