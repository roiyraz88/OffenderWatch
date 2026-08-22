// Parses the app's "DD/MM/YYYY, HH:mm" display format into a comparable Date.
function parseDisplayTimestamp(text) {
  const [datePart, timePart] = text.split(',').map((s) => s.trim());
  const [dd, mm, yyyy] = datePart.split('/').map(Number);
  const [hh, min] = timePart.split(':').map(Number);
  return new Date(yyyy, mm - 1, dd, hh, min);
}

module.exports = { parseDisplayTimestamp };
