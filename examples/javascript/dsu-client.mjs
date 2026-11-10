// Minimal JavaScript client for the DualSense Ultimate local API (Node 18+, no dependencies).
// Usage: node dsu-client.mjs <token> [port]
const [token, port = "26760"] = process.argv.slice(2);
if (!token) { console.error("Usage: node dsu-client.mjs <token> [port]"); process.exit(1); }
const base = `http://127.0.0.1:${port}/api/v1`;
const headers = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };

async function call(method, path, body) {
  const res = await fetch(base + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${await res.text()}`);
  return res.json();
}

export const dsu = {
  controllers: () => call("GET", "/controllers"),
  battery: (id = "first") => call("GET", `/controllers/${id}/battery`),
  lightbar: (color, id = "first", durationMs = 0) => call("POST", `/controllers/${id}/lightbar`, { color, durationMs }),
  rumble: (left, right, durationMs = 500, id = "first") => call("POST", `/controllers/${id}/rumble`, { left, right, durationMs }),
  triggerPreset: (preset, side = "both", id = "first") => call("POST", `/controllers/${id}/triggers`, { side, preset }),
  haptic: (pattern, id = "first") => call("POST", `/controllers/${id}/haptics`, { pattern }),
  profile: (profile, id = "first") => call("POST", `/controllers/${id}/profile`, { profile }),
  notify: (title) => call("POST", "/notify", { title }),
  reset: (id = "first") => call("POST", `/controllers/${id}/reset`, {}),
};

const pads = await dsu.controllers();
if (pads.length === 0) { console.log("No hay mandos conectados."); process.exit(0); }
for (const p of pads) console.log(`${p.name} [${p.id}] ${p.connection} - batería ${p.battery.percent} %`);

await dsu.lightbar("#8A5CFF", "first", 3000);
await dsu.triggerPreset("Arco", "R2");
await dsu.haptic("Latido");

// Live events (Server-Sent Events): print the first five, then clean up.
const res = await fetch(`${base}/events`, { headers });
const reader = res.body.getReader();
const decoder = new TextDecoder();
let seen = 0;
console.log("Esperando eventos (conecta/desconecta un mando o cambia de perfil)...");
while (seen < 5) {
  const { value, done } = await reader.read();
  if (done) break;
  for (const line of decoder.decode(value).split("\n")) {
    if (line.startsWith("data: ")) { console.log(JSON.parse(line.slice(6))); seen++; }
  }
}
await reader.cancel();
await dsu.reset();
