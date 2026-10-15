import { useState } from 'react'

export default function TabBar({ tabs, activeId, onSelect, onClose, onAdd, onRename }) {
  const [renamingId, setRenamingId] = useState(null)
  const [renameValue, setRenameValue] = useState('')

  function startRename(tab) {
    setRenamingId(tab.id)
    setRenameValue(tab.label)
  }

  function commitRename() {
    if (renameValue.trim()) onRename(renamingId, renameValue.trim())
    setRenamingId(null)
  }

  return (
    <div className="tab-bar">
      {tabs.map((tab) => (
        <div
          key={tab.id}
          className={tab.id === activeId ? 'tab tab-active' : 'tab'}
          onClick={() => onSelect(tab.id)}
        >
          {renamingId === tab.id ? (
            <input
              className="tab-rename-input"
              value={renameValue}
              autoFocus
              onChange={(e) => setRenameValue(e.target.value)}
              onBlur={commitRename}
              onKeyDown={(e) => { if (e.key === 'Enter') commitRename(); if (e.key === 'Escape') setRenamingId(null) }}
              onClick={(e) => e.stopPropagation()}
            />
          ) : (
            <span className="tab-label" onDoubleClick={(e) => { e.stopPropagation(); startRename(tab) }} title="Double-click to rename">
              {tab.label}
            </span>
          )}
          {tabs.length > 1 && (
            <button
              className="tab-close"
              onClick={(e) => { e.stopPropagation(); onClose(tab.id) }}
              aria-label={`Close ${tab.label}`}
            >
              ×
            </button>
          )}
        </div>
      ))}
      <button className="tab-add" onClick={onAdd} aria-label="New query tab">+</button>
    </div>
  )
}
