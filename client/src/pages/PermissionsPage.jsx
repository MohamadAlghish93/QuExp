import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import { SCREENS, RESTRICTABLE_SCREENS, ALWAYS_VISIBLE_SCREEN_KEYS } from '../constants/screens.js'
import ScreenTransferList from '../components/ScreenTransferList.jsx'

// Dynamic, per-user screen visibility - separate from the fixed Viewer/Editor/Admin
// role. Role controls what a user can DO (edit vs. view); this controls which
// screens even show up in their top nav. An empty list means "all screens".
export default function PermissionsPage() {
  const [users, setUsers] = useState([])
  const [selectedUsername, setSelectedUsername] = useState('')
  const [assignedKeys, setAssignedKeys] = useState([])
  const [showAll, setShowAll] = useState(true)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState(null)
  const [error, setError] = useState(null)

  async function load() {
    try {
      const list = await api.getAllUsersAdmin()
      setUsers(list)
      if (!selectedUsername && list.length) selectUser(list[0])
    } catch (e) {
      setError(e.message)
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  function selectUser(user) {
    setSelectedUsername(user.username)
    setAssignedKeys(user.allowedScreens || [])
    setShowAll((user.allowedScreens || []).length === 0)
    setMessage(null)
  }

  const selectedUser = users.find((u) => u.username === selectedUsername)

  async function handleSave() {
    setSaving(true)
    setMessage(null)
    setError(null)
    try {
      // "Show all screens" is represented server-side as an empty list.
      await api.updateUserScreens(selectedUsername, showAll ? [] : assignedKeys)
      setMessage('Saved.')
      load()
    } catch (e) {
      setError(e.message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="connections-page">
      <h2>Permissions</h2>
      <p className="muted">
        Choose which screens appear in each user's top navigation. This is independent of
        their Role (set on the Users page) — Role controls what they're allowed to do,
        this controls what they even see. Admins always see every screen regardless of
        this setting, so they can't lock themselves out of this page.
      </p>
      <p className="muted">
        {SCREENS.filter((s) => s.alwaysVisible).map((s) => s.label).join(' and ')} are always
        visible to every user by default and aren't listed below — everyone needs at least
        those to use the app at all.
      </p>

      {error && <div className="error-banner">{error}</div>}

      <div className="permissions-layout">
        <div className="connection-list">
          <h3>Users</h3>
          <ul className="favorite-list">
            {users.map((u) => (
              <li key={u.username}
                  className={u.username === selectedUsername ? 'favorite-card permission-user-active' : 'favorite-card'}
                  onClick={() => selectUser(u)}
                  style={{ cursor: 'pointer' }}>
                <div className="favorite-card-header">
                  <strong>{u.username}</strong>
                  <span className={u.role === 'Admin' ? 'badge' : 'badge badge-muted'}>{u.role}</span>
                </div>
                <p className="muted">
                  {u.role === 'Admin'
                    ? 'Always sees everything'
                    : (u.allowedScreens || []).length === 0
                      ? 'All screens (default)'
                      : `${u.allowedScreens.length} screen(s) visible`}
                </p>
              </li>
            ))}
          </ul>
        </div>

        <div className="connection-form">
          {!selectedUser ? (
            <p className="muted">Select a user.</p>
          ) : selectedUser.role === 'Admin' ? (
            <p className="muted">
              {selectedUser.username} is an Admin and always sees every screen. Change their
              role on the Users page first if you want to restrict what they see.
            </p>
          ) : (
            <>
              <h3>Screens for {selectedUsername}</h3>

              <label className="checkbox-label">
                <input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />
                Show all screens (default)
              </label>

              {!showAll && (
                <ScreenTransferList allScreens={RESTRICTABLE_SCREENS} assignedKeys={assignedKeys} onChange={setAssignedKeys} />
              )}

              {message && <p className="test-ok">{message}</p>}

              <div className="modal-actions">
                <button onClick={handleSave} disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
