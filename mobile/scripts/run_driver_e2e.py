#!/usr/bin/env python3
"""Opt-in Android/real HTTP/Valhalla/SQL E2E against an isolated local fixture."""
import argparse
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = "com.example.dalanmobile.e2e"
API_URL = "http://127.0.0.1:5072/"
PHASES = [
    "online_multiple_routes_and_maps_are_scoped_to_each_driver",
    "offline_queue_spans_two_routes_and_contains_real_jpeg_proof",
    "offline_queue_and_cached_routes_survive_process_restart",
    "reconnect_replays_once_and_switching_driver_hides_other_tasks",
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--device", required=True, help="Authorized adb serial")
    parser.add_argument("--adb", default=shutil.which("adb") or str(Path.home() / "Android/Sdk/platform-tools/adb"))
    parser.add_argument("--skip-build", action="store_true", help="Requires existing isolated E2E APKs and built backend tools")
    args = parser.parse_args()
    sql = os.environ.get("DALAN_MOBILE_E2E_SQL")
    provider = os.environ.get("DALAN_MOBILE_E2E_VALHALLA_URL")
    if not sql or not provider:
        parser.error("Set DALAN_MOBILE_E2E_SQL (fresh local DalanMobileE2E_* database) and DALAN_MOBILE_E2E_VALHALLA_URL.")
    with socket.socket() as check:
        if check.connect_ex(("127.0.0.1", 5072)) == 0:
            parser.error("Port 5072 is already in use; stop your own server before running this fixture.")
    adb = [args.adb, "-s", args.device]

    def run(command, **kwargs):
        return subprocess.run(command, check=True, cwd=ROOT, **kwargs)

    def shell(command, **kwargs):
        return run(adb + ["shell", command], **kwargs)

    def http(path, body=None, token=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        request = urllib.request.Request(API_URL.rstrip("/") + path,
            data=None if body is None else json.dumps(body).encode(), headers=headers)
        with urllib.request.urlopen(request, timeout=60) as response:
            data = response.read()
            return json.loads(data) if data else None

    if run(adb + ["get-state"], capture_output=True, text=True).stdout.strip() != "device":
        raise RuntimeError("Unlock the device and authorize USB debugging.")
    if not args.skip_build:
        run(["dotnet", "build", "backend/Dalan.sln", "--verbosity", "quiet"])
        subprocess.run(["./gradlew", ":app:assembleDebug", ":app:assembleDebugAndroidTest",
            "-PdalanE2E=true", "-PdalanDriverDebugApiUrl=" + API_URL, "--console=plain"],
            check=True, cwd=ROOT / "mobile")
    run(adb + ["install", "--user", "0", "-r", str(ROOT / "mobile/app/build/outputs/apk/debug/app-debug.apk")])
    run(adb + ["install", "--user", "0", "-r", str(ROOT / "mobile/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk")])

    with tempfile.TemporaryDirectory(prefix="dalan-mobile-e2e-") as temp:
        folder = Path(temp)
        manifest_path = folder / "fixture.json"
        env = os.environ.copy()
        env.update({"DALAN_MOBILE_E2E_MANIFEST": str(manifest_path)})
        run(["dotnet", "run", "--project", "backend/tools/Dalan.MobileE2E", "--no-build", "--", "seed"], env=env)
        fixture = json.loads(manifest_path.read_text())
        api_env = env.copy()
        api_env.update({"ASPNETCORE_URLS": API_URL.rstrip("/"), "ASPNETCORE_ENVIRONMENT": "Testing",
            "ConnectionStrings__Dalan": sql, "Valhalla__BaseUrl": provider,
            "Logging__LogLevel__Default": "Warning"})
        api = None

        def stop():
            nonlocal api
            if api is not None:
                api.terminate()
                try:
                    api.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    api.kill()
                    api.wait(timeout=5)
                api = None

        with (folder / "api.log").open("w") as log:
            def start():
                nonlocal api
                api = subprocess.Popen(["dotnet", str(ROOT / "backend/src/Dalan.InternalApi/bin/Debug/net9.0/Dalan.InternalApi.dll")],
                    cwd=ROOT / "backend/src/Dalan.InternalApi", env=api_env, stdout=log, stderr=subprocess.STDOUT)
                for _ in range(100):
                    if api.poll() is not None:
                        raise RuntimeError("The isolated API failed to start; inspect fixture configuration.")
                    try:
                        http("/api/driver/tasks")
                    except urllib.error.HTTPError as error:
                        if error.code == 401:
                            return
                        raise
                    except urllib.error.URLError:
                        time.sleep(.1)
                raise RuntimeError("The isolated API did not become ready.")

            def phase(method):
                shell("input keyevent KEYCODE_WAKEUP", capture_output=True)
                shell("wm dismiss-keyguard", capture_output=True)
                shell(f"am force-stop --user 0 {PACKAGE}", capture_output=True)
                result = shell("am instrument --user 0 -w -r -e class "
                    f"com.example.dalanmobile.DriverMultiRouteE2ETest#{method} "
                    f"{PACKAGE}.test/androidx.test.runner.AndroidJUnitRunner", capture_output=True, text=True)
                if ("OK (1 test)" not in result.stdout or "AssumptionViolatedException" in result.stdout
                    or "INSTRUMENTATION_STATUS_CODE: -3" in result.stdout):
                    raise RuntimeError(result.stdout + result.stderr)
                print("Passed: " + method, flush=True)

            try:
                start()
                staff = http("/api/auth/login", {"email": fixture["dispatcherEmail"], "password": fixture["password"]})["accessToken"]
                starts = [{"vehicleId": vehicle, "start": fixture["warehouse"]} for vehicle in fixture["vehicleIds"]]
                settings = {"warehouse": fixture["warehouse"], "serviceSecondsPerStop": 60}
                for batch in fixture["deliveryBatches"]:
                    preview = http("/api/dispatch-plans/preview", {"vehicles": starts, "deliveryIds": batch,
                        "strategy": "WarehouseClusterRotation", "settings": settings}, staff)
                    if preview["unassignedDeliveryIds"]:
                        raise RuntimeError("The live provider left fixture deliveries unassigned.")
                    http("/api/dispatch-plans", {"vehicles": starts,
                        "routes": [{"vehicleId": route["vehicleId"], "stops": [
                            {"deliveryId": stop["deliveryId"], "kind": stop["kind"]} for stop in route["stops"]]} for route in preview["routes"]],
                        "deliveryVersions": preview["deliveryVersions"], "strategy": preview["strategy"],
                        "settings": settings, "rotationVersion": preview["rotationVersion"]}, staff)
                run(adb + ["reverse", "tcp:5072", "tcp:5072"])
                shell(f"run-as {PACKAGE} mkdir -p files/dalan-e2e", capture_output=True)
                mobile = {"apiBaseUrl": API_URL, "driverEmail": fixture["driverEmails"][0],
                    "otherDriverEmail": fixture["driverEmails"][1], "password": fixture["password"], "initialTasks": 6}
                shell(f'run-as {PACKAGE} sh -c "cat > files/dalan-e2e/fixture.json"',
                    input=json.dumps(mobile).encode(), capture_output=True)
                phase(PHASES[0])
                stop()
                phase(PHASES[1])
                phase(PHASES[2])
                start()
                phase(PHASES[3])
                run(["dotnet", "run", "--project", "backend/tools/Dalan.MobileE2E", "--no-build", "--", "verify"], env=env)
            finally:
                stop()
                subprocess.run(adb + ["reverse", "--remove", "tcp:5072"], capture_output=True)
                subprocess.run(adb + ["shell", f"run-as {PACKAGE} rm -rf files/dalan-e2e"], capture_output=True)


if __name__ == "__main__":
    main()
