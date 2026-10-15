import { useState } from 'react'

// Classic dual-listbox "transfer list": pick items on either side, move them
// across with the arrow buttons, or double-click one item to move it alone.
// allScreens is the full [{key,label}] catalog; assignedKeys is the current
// list of keys visible to this user. onChange receives the new assignedKeys array.
export default function ScreenTransferList({ allScreens, assignedKeys, onChange }) {
  const [selectedAvailable, setSelectedAvailable] = useState([])
  const [selectedAssigned, setSelectedAssigned] = useState([])

  const available = allScreens.filter((s) => !assignedKeys.includes(s.key))
  const assigned = allScreens.filter((s) => assignedKeys.includes(s.key))

  function moveToAssigned(keys) {
    onChange([...assignedKeys, ...keys.filter((k) => !assignedKeys.includes(k))])
    setSelectedAvailable([])
  }
  function moveToAvailable(keys) {
    onChange(assignedKeys.filter((k) => !keys.includes(k)))
    setSelectedAssigned([])
  }

  function handleMultiSelect(e, setter) {
    setter([...e.target.selectedOptions].map((o) => o.value))
  }

  return (
    <div className="transfer-list">
      <div className="transfer-column">
        <label className="transfer-label">Available screens</label>
        <select multiple size={8} value={selectedAvailable}
                onChange={(e) => handleMultiSelect(e, setSelectedAvailable)}>
          {available.map((s) => (
            <option key={s.key} value={s.key} onDoubleClick={() => moveToAssigned([s.key])}>
              {s.label}
            </option>
          ))}
        </select>
      </div>

      <div className="transfer-controls">
        <button type="button" onClick={() => moveToAssigned(selectedAvailable)} disabled={!selectedAvailable.length} title="Add selected">→</button>
        <button type="button" onClick={() => moveToAssigned(available.map((s) => s.key))} disabled={!available.length} title="Add all">≫</button>
        <button type="button" onClick={() => moveToAvailable(assigned.map((s) => s.key))} disabled={!assigned.length} title="Remove all">≪</button>
        <button type="button" onClick={() => moveToAvailable(selectedAssigned)} disabled={!selectedAssigned.length} title="Remove selected">←</button>
      </div>

      <div className="transfer-column">
        <label className="transfer-label">Visible to this user</label>
        <select multiple size={8} value={selectedAssigned}
                onChange={(e) => handleMultiSelect(e, setSelectedAssigned)}>
          {assigned.map((s) => (
            <option key={s.key} value={s.key} onDoubleClick={() => moveToAvailable([s.key])}>
              {s.label}
            </option>
          ))}
        </select>
      </div>
    </div>
  )
}
