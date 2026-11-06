#!/usr/bin/env python3
"""
Usage aggregator for MoVibeSkua (schema 1).

Reads the `movibeskua_installs` collection with the Firebase Admin SDK, folds it into the shape the
dashboard renders, and writes:

    tools/dashboard/usage_data.json      full snapshot
    tools/dashboard/usage_data.js        same, as a window global so file:// works
    tools/dashboard/usage_history.json   rolling snapshot series (one per day)
    tools/dashboard/usage_history.js     same, as a window global

Setup (once):
    pip install firebase-admin
    a service account key for the `movibeskua` project (Firebase console > Project settings >
    Service accounts > Generate new private key), saved as serviceAccount.json at the repo root
    (gitignored). Elsewhere, pass it with --key or set MOVIBESKUA_SERVICE_ACCOUNT.

Usage:
    python tools/aggregate_usage.py
"""
import argparse
import collections
import datetime
import json
import os
import statistics
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DASHBOARD = os.path.join(ROOT, "tools", "dashboard")
PROJECT = "movibeskua"
COLLECTION = "movibeskua_installs"
UNKNOWN = "Unknown"
BUCKETS = ["d5", "d15", "d60", "d180", "dmax"]
SCREEN_FIELDS = ["visits", "ms", "quick", "idle"] + BUCKETS
ARMY_SIZES = ["ARMY_SIZE_2", "ARMY_SIZE_3", "ARMY_SIZE_4", "ARMY_SIZE_5_7", "ARMY_SIZE_8_PLUS"]
HISTORY_LIMIT = 400
DAY_FIELDS = ["fg", "run", "bot"]
TIME_CHART_DAYS = 60


def day_from_key(key):
    # Day keys are "dYYYYMMDD" (UTC): a plain identifier, so the client can write it in a field path.
    if len(key) != 9 or key[0] != "d" or not key[1:].isdigit():
        return None
    try:
        return datetime.date(int(key[1:5]), int(key[5:7]), int(key[7:9]))
    except ValueError:
        return None


def fold_time(documents, now, screens_ms):
    """Time spent in the app per UTC day and in total.

    fg = the app on screen, run = a client process alive (army clients and screen-off botting count
    too), bot = a script or Auto Attack/Hunt running. Each install's day counts as active when fg > 0.
    """
    per_day = {}
    install_totals = []
    for doc in documents:
        install_fg = 0
        for key, fields in (doc.get("days") or {}).items():
            day = day_from_key(key)
            if day is None or not isinstance(fields, dict):
                continue
            bucket = per_day.setdefault(day, {"fg": 0, "run": 0, "bot": 0, "users": 0})
            for field in DAY_FIELDS:
                bucket[field] += max(0, to_int(fields.get(field)))
            if to_int(fields.get("fg")) > 0:
                bucket["users"] += 1
            install_fg += max(0, to_int(fields.get("fg")))
        if install_fg > 0:
            install_totals.append(install_fg)

    today = now.date()
    rows = []
    cumulative = 0
    if per_day:
        first = min(per_day)
        day = first
        while day <= today:
            bucket = per_day.get(day, {"fg": 0, "run": 0, "bot": 0, "users": 0})
            cumulative += bucket["fg"]
            rows.append({
                "day": day.isoformat(),
                "minutes": round(bucket["fg"] / 60000.0, 1),
                "botMinutes": round(bucket["bot"] / 60000.0, 1),
                "runMinutes": round(bucket["run"] / 60000.0, 1),
                "users": bucket["users"],
                "avgMinutes": round(bucket["fg"] / 60000.0 / bucket["users"], 1) if bucket["users"] else 0,
                "cumulativeHours": round(cumulative / 3600000.0, 2),
            })
            day += datetime.timedelta(days=1)

    def window(days):
        recent = [row for row in rows if (today - datetime.date.fromisoformat(row["day"])).days < days]
        minutes = sum(row["minutes"] for row in recent)
        user_days = sum(row["users"] for row in recent)
        return round(minutes / user_days, 1) if user_days else None

    today_row = rows[-1] if rows and rows[-1]["day"] == today.isoformat() else None
    total_fg = sum(bucket["fg"] for bucket in per_day.values())
    install_totals.sort()
    return {
        "days": rows[-TIME_CHART_DAYS:],
        "todayMinutes": today_row["minutes"] if today_row else 0,
        "todayUsers": today_row["users"] if today_row else 0,
        "avgPerUserDay7": window(7),
        "avgPerUserDay30": window(30),
        "totalHours": round(total_fg / 3600000.0, 1),
        "totalMinutes": round(total_fg / 60000.0, 1),
        "botMinutes": round(sum(bucket["bot"] for bucket in per_day.values()) / 60000.0, 1),
        "botHours": round(sum(bucket["bot"] for bucket in per_day.values()) / 3600000.0, 1),
        "runHours": round(sum(bucket["run"] for bucket in per_day.values()) / 3600000.0, 1),
        "trackedInstalls": len(install_totals),
        "medianHoursPerInstall": round(statistics.median(install_totals) / 3600000.0, 1) if install_totals else None,
        # Page time from every version, before per-day time existed (each visit capped at 30 min).
        "pageHours": round(screens_ms / 3600000.0, 1),
    }


def as_datetime(value):
    if isinstance(value, datetime.datetime):
        return value if value.tzinfo else value.replace(tzinfo=datetime.timezone.utc)
    return None


def to_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def version_key(text):
    return [int(piece) if piece.isdigit() else 0 for piece in str(text).replace("-", ".").split(".")]


def fold(documents, now):
    installs = len(documents)
    windows = {"active1": 1, "active7": 7, "active30": 30}
    active = {name: 0 for name in windows}
    new_per_day = collections.Counter()
    versions = collections.Counter()
    active_versions = collections.Counter()
    countries = collections.Counter()
    active_countries = collections.Counter()
    features = collections.Counter()
    feature_installs = collections.Counter()
    largest_army = collections.Counter()
    screens = {}

    for doc in documents:
        last_seen = as_datetime(doc.get("lastSeen"))
        first_seen = as_datetime(doc.get("firstSeen"))
        version = str(doc.get("appVersion") or UNKNOWN)
        country = str(doc.get("country") or UNKNOWN)
        versions[version] += 1
        countries[country] += 1

        if last_seen is not None:
            age = now - last_seen
            for name, days in windows.items():
                if age <= datetime.timedelta(days=days):
                    active[name] += 1
            if age <= datetime.timedelta(days=30):
                active_versions[version] += 1
                active_countries[country] += 1

        if first_seen is not None and now - first_seen <= datetime.timedelta(days=30):
            new_per_day[first_seen.date().isoformat()] += 1

        doc_features = doc.get("features") or {}
        for signal, count in doc_features.items():
            count = to_int(count)
            if count > 0:
                features[signal] += count
                feature_installs[signal] += 1

        # The most clients each install ever ran at once ("Client 1 only" when it never added one).
        reached = [size for size in ARMY_SIZES if to_int(doc_features.get(size)) > 0]
        largest_army[reached[-1] if reached else "ARMY_SIZE_1"] += 1

        for screen, fields in (doc.get("screens") or {}).items():
            bucket = screens.setdefault(screen, collections.Counter())
            for field in SCREEN_FIELDS:
                bucket[field] += to_int((fields or {}).get(field))

    screen_rows = []
    for screen, total in screens.items():
        visits = total["visits"]
        if visits <= 0:
            continue
        long_visits = total["d180"] + total["dmax"]
        screen_rows.append({
            "screen": screen,
            "visits": visits,
            "minutes": round(total["ms"] / 60000.0, 1),
            "avgSeconds": round(total["ms"] / visits / 1000.0, 1),
            "quickExitRate": round(total["quick"] / visits, 4),
            "idleRate": round(total["idle"] / visits, 4),
            "longRate": round(long_visits / visits, 4),
            "attention": round(min(1.0, max(total["idle"], long_visits) / visits), 4),
            "dwell": {name: total[name] for name in BUCKETS},
        })
    screen_rows.sort(key=lambda row: (-row["attention"], -row["visits"]))

    feature_rows = [
        {
            "id": signal,
            "count": count,
            "installs": feature_installs[signal],
            "adoption": round(feature_installs[signal] / installs, 4) if installs else 0,
        }
        for signal, count in features.most_common()
    ]

    def ratio(done, started):
        return round(done / started, 4) if started else None

    flows = {
        "scriptErrorRate": ratio(features["SCRIPT_ERROR"], features["SCRIPT_STARTED"]),
        "kickedRelogins": ratio(features["RELOGIN_KICKED"], features["RELOGIN"] + features["RELOGIN_KICKED"]),
        "crashRate": ratio(features["SYS_CRASH"], features["SYS_SESSION_START"] + features["ARMY_CLIENT_START"]),
        "rendererLossRate": ratio(features["SYS_RENDERER_GONE"], features["SYS_SESSION_START"] + features["ARMY_CLIENT_START"]),
    }

    def ranked(counter):
        return [{"name": name, "installs": count} for name, count in counter.most_common()]

    return {
        "generatedAt": now.isoformat(),
        "installs": installs,
        "active": active,
        "newPerDay": [{"day": day, "installs": count} for day, count in sorted(new_per_day.items())],
        "versions": sorted(ranked(versions), key=lambda row: version_key(row["name"]), reverse=True),
        "activeVersions": sorted(ranked(active_versions), key=lambda row: version_key(row["name"]), reverse=True),
        "countries": ranked(countries),
        "activeCountries": ranked(active_countries),
        "largestArmy": [{"size": size, "installs": largest_army[size]} for size in ["ARMY_SIZE_1"] + ARMY_SIZES],
        "screens": screen_rows,
        "features": feature_rows,
        "flows": flows,
        "time": fold_time(documents, now, sum(total["ms"] for total in screens.values())),
    }


def write_outputs(data):
    os.makedirs(DASHBOARD, exist_ok=True)
    with open(os.path.join(DASHBOARD, "usage_data.json"), "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2)
    with open(os.path.join(DASHBOARD, "usage_data.js"), "w", encoding="utf-8") as handle:
        handle.write("window.USAGE_DATA = " + json.dumps(data) + ";\n")

    history_path = os.path.join(DASHBOARD, "usage_history.json")
    history = []
    if os.path.exists(history_path):
        try:
            with open(history_path, encoding="utf-8") as handle:
                history = json.load(handle)
        except (OSError, ValueError):
            history = []
    today = data["generatedAt"][:10]
    entry = {
        "day": today,
        "installs": data["installs"],
        "active1": data["active"]["active1"],
        "active7": data["active"]["active7"],
        "active30": data["active"]["active30"],
        "todayMinutes": data["time"]["todayMinutes"],
        "totalHours": data["time"]["totalHours"],
    }
    history = [row for row in history if row.get("day") != today] + [entry]
    history = history[-HISTORY_LIMIT:]
    with open(history_path, "w", encoding="utf-8") as handle:
        json.dump(history, handle, indent=2)
    with open(os.path.join(DASHBOARD, "usage_history.js"), "w", encoding="utf-8") as handle:
        handle.write("window.USAGE_HISTORY = " + json.dumps(history) + ";\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--key", default=os.environ.get("MOVIBESKUA_SERVICE_ACCOUNT") or os.path.join(ROOT, "serviceAccount.json"))
    args = parser.parse_args()

    if not os.path.exists(args.key):
        sys.exit("service account key not found: " + args.key + " (pass --key)")
    try:
        import firebase_admin
        from firebase_admin import credentials, firestore
    except ImportError:
        sys.exit("pip install firebase-admin")

    certificate = credentials.Certificate(args.key)
    # Another project's key would read an empty collection there and report no installs.
    if certificate.project_id != PROJECT:
        sys.exit(args.key + " is a key for " + str(certificate.project_id) + ", not " + PROJECT)
    firebase_admin.initialize_app(certificate)
    db = firestore.client()
    documents = [snapshot.to_dict() or {} for snapshot in db.collection(COLLECTION).stream()]

    data = fold(documents, datetime.datetime.now(datetime.timezone.utc))
    write_outputs(data)
    print("installs: " + str(data["installs"]) + ", active 7d: " + str(data["active"]["active7"]))


if __name__ == "__main__":
    main()
