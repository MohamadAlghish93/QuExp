// Lightweight SQL beautifier - no external library. Good enough for typical
// SELECT/JOIN/WHERE/GROUP BY queries; not a full parser, so unusual syntax
// (nested subqueries with the same keywords, CASE expressions) may format
// imperfectly. Users can still hand-edit after formatting.

const NEWLINE_BEFORE = [
  'SELECT', 'FROM', 'WHERE', 'GROUP BY', 'ORDER BY', 'HAVING',
  'LEFT JOIN', 'RIGHT JOIN', 'INNER JOIN', 'FULL JOIN', 'JOIN',
  'UNION ALL', 'UNION', 'LIMIT', 'OFFSET', 'FETCH FIRST', 'FETCH NEXT',
]

const INDENT_AFTER = ['SELECT', 'WHERE', 'GROUP BY', 'ORDER BY', 'HAVING', 'ON']

const KEYWORDS = [
  ...NEWLINE_BEFORE, 'AND', 'OR', 'ON', 'AS', 'IN', 'NOT', 'NULL', 'IS',
  'BETWEEN', 'LIKE', 'DISTINCT', 'TOP', 'COUNT', 'SUM', 'AVG', 'MIN', 'MAX',
  'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'ASC', 'DESC', 'EXISTS',
]

export function formatSql(sql) {
  if (!sql || !sql.trim()) return sql

  // Collapse whitespace first so we start from a clean single-line string.
  let normalized = sql.replace(/\s+/g, ' ').trim()

  // Uppercase known keywords (longest first, so "GROUP BY" wins before "GROUP").
  const sortedKeywords = [...new Set(KEYWORDS)].sort((a, b) => b.length - a.length)
  for (const kw of sortedKeywords) {
    const re = new RegExp(`\\b${kw.replace(' ', '\\s+')}\\b`, 'gi')
    normalized = normalized.replace(re, kw)
  }

  // Break onto new lines before major clauses.
  const sortedBreaks = [...NEWLINE_BEFORE].sort((a, b) => b.length - a.length)
  for (const kw of sortedBreaks) {
    const re = new RegExp(`\\s+${kw}\\b`, 'g')
    normalized = normalized.replace(re, `\n${kw}`)
  }

  // Put each comma-separated column on its own indented line, but only
  // inside the SELECT list (before the first FROM) - splitting every comma
  // in the query would also break up function args and IN(...) lists.
  const fromIndex = normalized.indexOf('\nFROM')
  if (normalized.startsWith('SELECT') && fromIndex > -1) {
    const selectPart = normalized.slice(0, fromIndex)
    const rest = normalized.slice(fromIndex)
    const columns = splitTopLevelCommas(selectPart.replace(/^SELECT\s*/, ''))
    normalized = 'SELECT\n  ' + columns.map((c) => c.trim()).join(',\n  ') + rest
  }

  // Indent lines that follow WHERE/ON/GROUP BY/etc.
  const lines = normalized.split('\n')
  let indentNext = false
  const output = lines.map((line) => {
    const trimmed = line.trim()
    const isClauseStart = NEWLINE_BEFORE.some((kw) => trimmed.startsWith(kw))
    const result = (indentNext && !isClauseStart) ? '  ' + trimmed : trimmed
    indentNext = INDENT_AFTER.some((kw) => trimmed === kw || trimmed.startsWith(kw + ' ') || trimmed.startsWith(kw + '\n'))
    return result
  })

  return output.join('\n').trim()
}

// Splits on commas that are not inside parentheses, so `COUNT(a, b)` or
// `IN (1, 2, 3)` doesn't get split into separate "columns".
function splitTopLevelCommas(text) {
  const parts = []
  let depth = 0
  let current = ''
  for (const char of text) {
    if (char === '(') depth++
    if (char === ')') depth--
    if (char === ',' && depth === 0) {
      parts.push(current)
      current = ''
    } else {
      current += char
    }
  }
  if (current.trim()) parts.push(current)
  return parts
}
