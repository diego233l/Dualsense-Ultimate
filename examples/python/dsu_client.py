"""Minimal Python client for the DualSense Ultimate local API.

Usage:  python dsu_client.py <token> [port]
The token is shown in the application under API. Only the standard library is used.
"""
import json
import sys
import time
import urllib.request


class DualSenseUltimate:
    def __init__(self, token: str, port: int = 26760):
        self.base = f"http://127.0.0.1:{port}/api/v1"
        self.headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}

    def _call(self, method: str, path: str, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, headers=self.headers, method=method)
        with urllib.request.urlopen(req, timeout=5) as resp:
            return json.loads(resp.read().decode())

    def controllers(self):
        return self._call("GET", "/controllers")

    def battery(self, controller="first"):
        return self._call("GET", f"/controllers/{controller}/battery")

    def lightbar(self, color: str, controller="first", duration_ms: int = 0):
        return self._call("POST", f"/controllers/{controller}/lightbar", {"color": color, "durationMs": duration_ms})

    def rumble(self, left: int, right: int, duration_ms: int = 500, controller="first"):
        return self._call("POST", f"/controllers/{controller}/rumble", {"left": left, "right": right, "durationMs": duration_ms})

    def trigger_preset(self, preset: str, side: str = "both", controller="first"):
        return self._call("POST", f"/controllers/{controller}/triggers", {"side": side, "preset": preset})

    def trigger_effect(self, effect: dict, side: str = "both", controller="first"):
        """effect example: {"type": "Weapon", "start": 2, "end": 5, "strength": 8}"""
        return self._call("POST", f"/controllers/{controller}/triggers", {"side": side, "effect": effect})

    def haptic(self, pattern: str, controller="first"):
        return self._call("POST", f"/controllers/{controller}/haptics", {"pattern": pattern})

    def profile(self, name: str, controller="first"):
        return self._call("POST", f"/controllers/{controller}/profile", {"profile": name})

    def notify(self, title: str):
        return self._call("POST", "/notify", {"title": title})

    def reset(self, controller="first"):
        return self._call("POST", f"/controllers/{controller}/reset", {})


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    dsu = DualSenseUltimate(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 26760)
    pads = dsu.controllers()
    if not pads:
        sys.exit("No hay mandos conectados.")
    for pad in pads:
        print(f"{pad['name']} [{pad['id']}] {pad['connection']} - batería {pad['battery']['percent']} % ({pad['battery']['status']})")
    # Example of a game integration: trigger resistance follows a (fake) ammo counter.
    for ammo in range(8, 0, -1):
        dsu.trigger_effect({"type": "Weapon", "start": 2, "end": 5, "strength": ammo}, side="R2")
        dsu.lightbar("#00FF00" if ammo > 3 else "#FF0000")
        time.sleep(0.4)
    dsu.haptic("Impacto")
    time.sleep(1)
    dsu.reset()
