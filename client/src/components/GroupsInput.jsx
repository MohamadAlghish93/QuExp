import CloudscapeInput from './CloudscapeInput.jsx'
import CloudscapeButton from './CloudscapeButton.jsx'
import { useId, useState } from 'react'

// Several groups for one user, entered as separate chips. Type a name and press Enter or comma
// (or just click away) to add it; Backspace on an empty box removes the last one. Existing group
// names are offered as suggestions so "Sales" and "sales" don't turn into two different groups.
export default function GroupsInput({ value, onChange, suggestions = [] }) {
  const [text, setText] = useState('')
  const listId = useId()

  function add(raw) {
    const group = raw.trim()
    setText('')
    if (!group || value.some((g) => g.toLowerCase() === group.toLowerCase())) return
    // Reuse the existing spelling if the group already exists elsewhere.
    const canonical = suggestions.find((g) => g.toLowerCase() === group.toLowerCase()) || group
    onChange([...value, canonical])
  }

  function handleKeyDown(e) {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault()
      add(text)
    } else if (e.key === 'Backspace' && text === '' && value.length > 0) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <div className="groups-input">
      <div className="tag-input-wrapper">
        {value.map((g) => (
          <span key={g} className="tag tag-removable">
            {g}
            <CloudscapeButton type="button" onClick={() => onChange(value.filter((x) => x !== g))} aria-label={`Remove group ${g}`}>×</CloudscapeButton>
          </span>
        ))}
        <CloudscapeInput
          className="tag-input"
          value={text}
          list={listId}
          placeholder={value.length ? 'Add another…' : 'Type a group, press Enter'}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={handleKeyDown}
          onBlur={() => add(text)}
        />
      </div>
      <datalist id={listId}>
        {suggestions.filter((g) => !value.some((v) => v.toLowerCase() === g.toLowerCase())).map((g) => <option key={g} value={g} />)}
      </datalist>
    </div>
  )
}
