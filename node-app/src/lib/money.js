// Orijinal server.js'deki money(value) yardımcısının aynısı (bu zaten JS olduğu için
// C# portundaki gibi Number()/toFixed(2) davranışını taklit etmeye gerek yok).
export function money(value) {
  const formatted = Number(value).toFixed(2);
  return formatted.endsWith(".00") ? formatted.slice(0, -3) : formatted;
}
