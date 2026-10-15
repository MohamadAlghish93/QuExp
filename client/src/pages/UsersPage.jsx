import { useEffect, useMemo, useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'

const ROLE_DESCRIPTIONS = {
  Viewer: 'Can run queries against connections they have access to, use favorites/templates. Can\'t manage connections or users.',
  Editor: 'Everything a Viewer can, plus create/edit connections, add templates to the shared library, manage own alerts.',
  Admin: 'Everything, including managing users and roles, and seeing every connection regardless of restrictions.',
}

const EMPTY_FORM = { username: '', password: '', role: 'Viewer', displayName: '', email: '', groupName: '' }
const UNGROUPED = '(ungrouped)'

export default function UsersPage() {
  const { username: myUsername } = useAuth()
  const [users, setUsers] = useState([])
  const [groups, setGroups] = useState([])
  const [selectedGroup, setSelectedGroup] = useState('')
  const [form, setForm] = useState(EMPTY_FORM)
  const [error, setError] = useState(null)
  const [saving, setSaving] = useState(false)

  const [editingUsername, setEditingUsername] = useState(null)
  const [editForm, setEditForm] = useState(null)

  const [passwordEditUsername, setPasswordEditUsername] = useState(null)
  const [newPassword, setNewPassword] = useState('')
  const [passwordMessage, setPasswordMessage] = useState(null)

  async function load() {
    try {
      const [userList, groupList] = await Promise.all([api.getAllUsersAdmin(), api.getUserGroups()])
      setUsers(userList)
      setGroups(groupList)
    } catch (e) {
      setError(e.message)
    }
  }

  useEffect(() => { load() }, [])

  const usersByGroup = useMemo(() => {
    const map = {}
    for (const u of users) {
      const key = u.groupName || UNGROUPED
      if (!map[key]) map[key] = []
      map[key].push(u)
    }
    return map
  }, [users])

  const visibleUsers = selectedGroup ? (usersByGroup[selectedGroup] || []) : users

  async function handleCreate(e) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await api.createUser(form)
      setForm(EMPTY_FORM)
      load()
    } catch (e) {
      setError(e.message)
    } finally {
      setSaving(false)
    }
  }

  async function handleRoleChange(username, role) {
    try {
      await api.updateUserRole(username, role)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  function startEdit(user) {
    setEditingUsername(user.username)
    setEditForm({
      displayName: user.displayName || '',
      email: user.email || '',
      groupName: user.groupName || '',
    })
  }

  async function handleSaveEdit(username) {
    try {
      await api.updateUserProfile(username, editForm)
      setEditingUsername(null)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  function startPasswordEdit(username) {
    setPasswordEditUsername(username)
    setNewPassword('')
    setPasswordMessage(null)
  }

  async function handlePasswordSave(username) {
    if (newPassword.length < 8) {
      setPasswordMessage('Password must be at least 8 characters.')
      return
    }
    try {
      await api.updateUserPassword(username, newPassword)
      setPasswordMessage('Password updated.')
      setNewPassword('')
      setTimeout(() => setPasswordEditUsername(null), 1200)
    } catch (e) {
      setPasswordMessage(e.message)
    }
  }

  async function handleDelete(username) {
    if (!confirm(`Delete user "${username}"? This cannot be undone.`)) return
    try {
      await api.deleteUser(username)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  return (
    <div className="connections-page">
      <h2>Users</h2>
      <p className="muted">
        Control who can log in, what role they have, and which group they belong to.
        Per-screen visibility is set separately on the Permissions page.
      </p>

      {error && <div className="error-banner">{error}</div>}

      <div className="connections-layout">
        <form className="connection-form" onSubmit={handleCreate}>
          <h3>New user</h3>
          <label>
            Username
            <input value={form.username} onChange={(e) => setForm((f) => ({ ...f, username: e.target.value }))} required />
          </label>
          <label>
            Password
            <input type="password" value={form.password} onChange={(e) => setForm((f) => ({ ...f, password: e.target.value }))}
                   required minLength={8} autoComplete="new-password" />
          </label>
          <label>
            Role
            <select value={form.role} onChange={(e) => setForm((f) => ({ ...f, role: e.target.value }))}>
              <option value="Viewer">Viewer</option>
              <option value="Editor">Editor</option>
              <option value="Admin">Admin</option>
            </select>
          </label>
          <p className="field-hint hint-existing">{ROLE_DESCRIPTIONS[form.role]}</p>

          <label>
            Display name (optional)
            <input value={form.displayName} onChange={(e) => setForm((f) => ({ ...f, displayName: e.target.value }))} />
          </label>
          <label>
            Email (optional)
            <input type="email" value={form.email} onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))} />
          </label>
          <label>
            Group (optional)
            <input value={form.groupName} onChange={(e) => setForm((f) => ({ ...f, groupName: e.target.value }))}
                   list="existing-groups" placeholder="Pick an existing group or type a new name" />
            <datalist id="existing-groups">
              {groups.map((g) => <option key={g} value={g} />)}
            </datalist>
          </label>

          <button type="submit" disabled={saving}>{saving ? 'Creating…' : 'Create user'}</button>
        </form>

        <div className="users-list-layout">
          <aside className="category-sidebar">
            <h4>Groups</h4>
            <ul>
              <li>
                <button className={selectedGroup === '' ? 'category-item active' : 'category-item'} onClick={() => setSelectedGroup('')}>
                  <span>All users</span>
                  <span className="count-badge">{users.length}</span>
                </button>
              </li>
              {Object.keys(usersByGroup).sort().map((group) => (
                <li key={group}>
                  <button className={selectedGroup === group ? 'category-item active' : 'category-item'} onClick={() => setSelectedGroup(group)}>
                    <span title={group}>{group}</span>
                    <span className="count-badge">{usersByGroup[group].length}</span>
                  </button>
                </li>
              ))}
            </ul>
          </aside>

          <div className="connection-list">
            <h3>Users{selectedGroup ? ` — ${selectedGroup}` : ''}</h3>
            <ul className="favorite-list">
              {visibleUsers.map((u) => (
                <li key={u.username} className="favorite-card">
                  <div className="favorite-card-header">
                    <strong>
                      {u.displayName ? `${u.displayName} (${u.username})` : u.username}
                      {u.username === myUsername ? ' — you' : ''}
                    </strong>
                    {u.groupName && <span className="badge badge-muted">{u.groupName}</span>}
                  </div>
                  {u.email && <p className="muted connection-detail">{u.email}</p>}
                  <p className="muted">{new Date(u.createdAtUtc).toLocaleDateString()}</p>

                  <div className="form-row" style={{ marginTop: 8 }}>
                    <select value={u.role} onChange={(e) => handleRoleChange(u.username, e.target.value)}
                            disabled={u.username === myUsername}>
                      <option value="Viewer">Viewer</option>
                      <option value="Editor">Editor</option>
                      <option value="Admin">Admin</option>
                    </select>
                    <button className="secondary" onClick={() => startEdit(u)}>Edit profile</button>
                    <button className="secondary" onClick={() => startPasswordEdit(u.username)}>Change password</button>
                    <button className="secondary danger" onClick={() => handleDelete(u.username)}
                            disabled={u.username === myUsername}>
                      Delete
                    </button>
                  </div>

                  {editingUsername === u.username && (
                    <div className="edit-profile-panel">
                      <label>
                        Display name
                        <input value={editForm.displayName} onChange={(e) => setEditForm((f) => ({ ...f, displayName: e.target.value }))} />
                      </label>
                      <label>
                        Email
                        <input type="email" value={editForm.email} onChange={(e) => setEditForm((f) => ({ ...f, email: e.target.value }))} />
                      </label>
                      <label>
                        Group
                        <input value={editForm.groupName} onChange={(e) => setEditForm((f) => ({ ...f, groupName: e.target.value }))}
                               list="existing-groups" />
                      </label>
                      <div className="modal-actions">
                        <button type="button" className="secondary" onClick={() => setEditingUsername(null)}>Cancel</button>
                        <button type="button" onClick={() => handleSaveEdit(u.username)}>Save</button>
                      </div>
                    </div>
                  )}

                  {passwordEditUsername === u.username && (
                    <div className="form-row" style={{ marginTop: 8 }}>
                      <input type="password" className="grow" placeholder="New password (min 8 chars)"
                             value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
                             autoComplete="new-password" />
                      <button className="secondary" onClick={() => handlePasswordSave(u.username)}>Save</button>
                      <button className="secondary" onClick={() => setPasswordEditUsername(null)}>Cancel</button>
                    </div>
                  )}
                  {passwordEditUsername === u.username && passwordMessage && (
                    <p className={passwordMessage === 'Password updated.' ? 'test-ok' : 'error-banner'}>{passwordMessage}</p>
                  )}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </div>
  )
}
