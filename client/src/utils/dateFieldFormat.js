// Parses a fixed-width raw date string (e.g. "20260809220911") using a source
// format made of positional tokens (yyyy, MM, dd, HH, mm, ss - same meaning as
// .NET custom date formats), then renders it back out using a display format
// built from the same tokens plus any literal characters (/, -, :, space, etc).
// Not a general date parser - it assumes the raw value has no separators and is
// exactly as wide as the source format describes, which matches the kind of
// value this feature is for (a raw yyyyMMddHHmmss-style column).

const TOKEN_REGEX = /(yyyy|yy|MM|dd|HH|mm|ss)/g

function extractTokens(sourceFormat) {
  return sourceFormat.match(TOKEN_REGEX) || []
}

export function parseFixedWidthDate(raw, sourceFormat) {
  if (raw === null || raw === undefined) return null
  const text = String(raw).trim()
  const tokens = extractTokens(sourceFormat)
  if (tokens.length === 0) return null

  const parts = {}
  let index = 0
  for (const token of tokens) {
    const len = token.length === 4 ? 4 : 2
    const chunk = text.slice(index, index + len)
    index += len
    if (!/^\d+$/.test(chunk)) return null // not a clean digit run at this position - bail out

    if (token === 'yyyy' || token === 'yy') parts.year = token === 'yy' ? 2000 + Number(chunk) : Number(chunk)
    else if (token === 'MM') parts.month = Number(chunk)
    else if (token === 'dd') parts.day = Number(chunk)
    else if (token === 'HH') parts.hour = Number(chunk)
    else if (token === 'mm') parts.minute = Number(chunk)
    else if (token === 'ss') parts.second = Number(chunk)
  }

  if (!parts.year || !parts.month || !parts.day) return null
  return {
    year: parts.year, month: parts.month, day: parts.day,
    hour: parts.hour ?? 0, minute: parts.minute ?? 0, second: parts.second ?? 0,
  }
}

function pad(n, width) { return String(n).padStart(width, '0') }

export function formatParsedDate(parsed, displayFormat) {
  if (!parsed) return null
  let result = ''
  let i = 0
  while (i < displayFormat.length) {
    const rest = displayFormat.slice(i)
    const match = TOKEN_REGEX.exec(rest)
    TOKEN_REGEX.lastIndex = 0
    if (match && match.index === 0) {
      const token = match[0]
      if (token === 'yyyy') result += pad(parsed.year, 4)
      else if (token === 'yy') result += pad(parsed.year % 100, 2)
      else if (token === 'MM') result += pad(parsed.month, 2)
      else if (token === 'dd') result += pad(parsed.day, 2)
      else if (token === 'HH') result += pad(parsed.hour, 2)
      else if (token === 'mm') result += pad(parsed.minute, 2)
      else if (token === 'ss') result += pad(parsed.second, 2)
      i += token.length
    } else {
      result += displayFormat[i]
      i += 1
    }
  }
  return result
}

/// Convenience wrapper: raw value in, display string out (or the original raw value,
/// unchanged, if it doesn't match the expected source format - so a formatting
/// mistake or an unrelated value never shows as blank or throws).
export function formatDateField(raw, sourceFormat, displayFormat) {
  const parsed = parseFixedWidthDate(raw, sourceFormat)
  if (!parsed) return raw
  return formatParsedDate(parsed, displayFormat) ?? raw
}
