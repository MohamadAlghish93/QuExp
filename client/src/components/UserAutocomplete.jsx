import { useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api/client.js'

// Typeahead user picker, used everywhere a plain <select> or checkbox list of
// users used to be (assignee pickers, connection/template access lists).
// multi=false: single value in/out via value/onChange (strings).
// multi=true: array of usernames in/out via value/onChange, rendered as chips.
export default function UserAutocomplete({ value, onChange, multi = false, excludeUsernames = [], placeholder }) {
  const [allUsers, setAllUsers] = useState([])
  const [inputValue, setInputValue] = useState(multi ? '' : (value || ''))
  const [open, setOpen] = useState(false)
  const containerRef = useRef(null)

  useEffect(() => {
    api.getUsers().then((list) => setAllUsers(list.map((u) => u.username))).catch(() => {})
  }, [])

  useEffect(() => {
    if (!multi) setInputValue(value || '')
  }, [value, multi])

  useEffect(() => {
    function handleClickOutside(e) {
      if (containerRef.current && !containerRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const selectedList = multi ? (value || []) : []

  const suggestions = useMemo(() => {
    const needle = inputValue.trim().toLowerCase()
    return allUsers
      .filter((u) => !excludeUsernames.includes(u))
      .filter((u) => !multi || !selectedList.includes(u))
      .filter((u) => needle === '' || u.toLowerCase().includes(needle))
      .slice(0, 8)
  }, [allUsers, inputValue, excludeUsernames, multi, selectedList])

  function selectUser(username) {
    if (multi) {
      onChange([...selectedList, username])
      setInputValue('')
    } else {
      onChange(username)
      setInputValue(username)
    }
    setOpen(false)
  }

  function removeChip(username) {
    onChange(selectedList.filter((u) => u !== username))
  }

  return (
    <div className="user-autocomplete" ref={containerRef}>
      {multi && selectedList.length > 0 && (
        <div className="tag-list">
          {selectedList.map((u) => (
            <span key={u} className="tag tag-removable">
              @{u}
              <button type="button" onClick={() => removeChip(u)} aria-label={`Remove ${u}`}>×</button>
            </span>
          ))}
        </div>
      )}
      <input
        value={inputValue}
        onChange={(e) => { setInputValue(e.target.value); setOpen(true); if (!multi) onChange(e.target.value) }}
        onFocus={() => setOpen(true)}
        placeholder={placeholder || 'Type a username…'}
        autoComplete="off"
      />
      {open && suggestions.length > 0 && (
        <div className="user-autocomplete-menu">
          {suggestions.map((u) => (
            <button type="button" key={u} className="user-autocomplete-option" onClick={() => selectUser(u)}>
              @{u}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
