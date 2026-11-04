import CloudscapeButton from '../components/CloudscapeButton.jsx'
import Checkbox from '@cloudscape-design/components/checkbox'
import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import { SCREENS, RESTRICTABLE_SCREENS } from '../constants/screens.js'
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
    <div className="connections-page permissions-page">
      <header className="permissions-header">
        <div>
          <h2>Permissions</h2>
          <p className="muted">
            Manage which areas appear in each user's navigation. This setting controls visibility only;
            a user's Viewer, Editor, or Admin role determines what actions they can perform.
          </p>
        </div>
      </header>

      {error && <div className="error-banner">{error}</div>}

      <div className="permissions-layout">
        <div className="connection-list">
          <div className="permissions-list-heading">
            <h3>Select a user</h3>
            <span className="muted">{users.length}</span>
          </div>
          <p className="muted permissions-help">Choose an account to review its navigation access.</p>
          <ul className="favorite-list permissions-user-list">
            {users.map((u) => (
              <li key={u.username}>
                <CloudscapeButton
                  className={u.username === selectedUsername ? 'permission-user-card permission-user-active' : 'permission-user-card'}
                  onClick={() => selectUser(u)}
                  aria-pressed={u.username === selectedUsername}
                >
                  <span className="permission-user-identity">
                    <strong>{u.displayName || u.username}</strong>
                    <span className="muted">@{u.username}</span>
                  </span>
                  <span className={u.role === 'Admin' ? 'badge' : 'badge badge-muted'}>{u.role}</span>
                  <span className="permission-user-summary">
                    {u.role === 'Admin'
                      ? 'Always sees every screen'
                      : (u.allowedScreens || []).length === 0
                        ? 'All restrictable screens'
                        : `${u.allowedScreens.length} additional screen(s)`}
                  </span>
                </CloudscapeButton>
              </li>
            ))}
          </ul>
        </div>

        <div className="connection-form">
          {!selectedUser ? (
            <div className="permissions-empty">
              <h3>Select a user</h3>
              <p className="muted">Choose an account to manage its navigation access.</p>
            </div>
          ) : selectedUser.role === 'Admin' ? (
            <div className="permissions-admin-note">
              <h3>{selectedUser.displayName || selectedUser.username}</h3>
              <span className="badge">Admin</span>
              <p className="muted">
                Admin accounts always see every screen, regardless of navigation settings. To restrict
                this account, change its role on the Users page first.
              </p>
            </div>
          ) : (
            <>
              <div className="permissions-detail-heading">
                <div>
                  <h3>Navigation access</h3>
                  <p className="muted">@{selectedUsername} · {selectedUser.role}</p>
                </div>
                <span className="badge badge-muted">{showAll ? 'Default' : `${assignedKeys.length} selected`}</span>
              </div>
              <p className="muted permissions-help">
                Select which additional pages this user can see. Core pages remain available to everyone.
              </p>
              <div className="permissions-default-option">
                <Checkbox checked={showAll} onChange={({ detail }) => setShowAll(detail.checked)}>
                  Show all pages (recommended)
                </Checkbox>
                <span className="muted">New pages are included automatically when this is selected.</span>
              </div>

              {!showAll && (
                <ScreenTransferList allScreens={RESTRICTABLE_SCREENS} assignedKeys={assignedKeys} onChange={setAssignedKeys} />
              )}
              <div className="permissions-always-visible">
                <strong>Always available</strong>
                <span>{SCREENS.filter((s) => s.alwaysVisible).map((s) => s.label).join(' · ')}</span>
              </div>

              {message && <p className="test-ok">{message}</p>}

              <div className="modal-actions">
                <CloudscapeButton iconName="check" onClick={handleSave} disabled={saving}>{saving ? 'Saving…' : 'Save changes'}</CloudscapeButton>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
