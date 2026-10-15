// Lets a user attach a plain-language meaning to a coded value (e.g. status
// code "1" -> "Pending") shown in a group-by result. Purely a local display
// aid - stored in the browser per connection+column, not sent to the server,
// since it's just "what does this code mean to me/my team" rather than data.
const STORAGE_KEY = 'qe_value_legend'

function loadAll() {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}')
  } catch {
    return {}
  }
}

function legendKey(connectionId, column) {
  return `${connectionId}::${column}`
}

export function getLegend(connectionId, column) {
  const all = loadAll()
  return all[legendKey(connectionId, column)] || {}
}

export function setLegendEntry(connectionId, column, value, meaning) {
  const all = loadAll()
  const key = legendKey(connectionId, column)
  const forColumn = { ...(all[key] || {}) }
  const valueKey = String(value)
  if (meaning.trim()) {
    forColumn[valueKey] = meaning.trim()
  } else {
    delete forColumn[valueKey]
  }
  all[key] = forColumn
  localStorage.setItem(STORAGE_KEY, JSON.stringify(all))
}
