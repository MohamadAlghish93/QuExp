import { useEffect, useState } from 'react'
import { api } from '../api/client.js'
import { useAuth } from '../auth/AuthContext.jsx'
import UserAutocomplete from '../components/UserAutocomplete.jsx'

const EMPTY_FORM = {
  id: null,
  name: '',
  providerType: 'SqlServer',
  host: 'localhost',
  port: 1433,
  database: '',
  username: '',
  password: '',
  extraOptions: '',
  allowedUsernames: [], // empty = visible to everyone
}

const DEFAULT_PORTS = { SqlServer: 1433, Oracle: 1521 }

export default function ConnectionsPage() {
  const { canEditConnections } = useAuth()
  const [connections, setConnections] = useState([])
  const [form, setForm] = useState(EMPTY_FORM)
  const [editing, setEditing] = useState(false)
  const [testResult, setTestResult] = useState(null)
  const [testing, setTesting] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)

  async function load() {
    try {
      setConnections(await api.getConnections())
    } catch (e) {
      setError(e.message)
    }
  }

  useEffect(() => { load() }, [])

  function update(field, value) {
    setForm((f) => {
      const next = { ...f, [field]: value }
      // Swap in the conventional default port when the database type changes,
      // unless the user already typed a custom one.
      if (field === 'providerType' && (f.port === DEFAULT_PORTS[f.providerType] || !f.port)) {
        next.port = DEFAULT_PORTS[value]
      }
      return next
    })
    setTestResult(null)
  }

  function startEdit(conn) {
    // The API never returns the stored password, so the field starts blank;
    // leaving it blank on save keeps the existing one.
    setForm({ ...conn, password: '', extraOptions: conn.extraOptions || '', allowedUsernames: conn.allowedUsernames || [] })
    setEditing(true)
    setTestResult(null)
    setError(null)
  }

  function resetForm() {
    setForm(EMPTY_FORM)
    setEditing(false)
    setTestResult(null)
  }

  async function handleTest() {
    setTesting(true)
    setTestResult(null)
    setError(null)
    try {
      const result = await api.testConnection(form)
      setTestResult(result)
    } catch (e) {
      setError(e.message)
    } finally {
      setTesting(false)
    }
  }

  async function handleSave(e) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await api.saveConnection({ ...form, port: Number(form.port) || 0 })
      resetForm()
      load()
    } catch (e) {
      setError(e.message)
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(id) {
    if (!confirm('Delete this connection?')) return
    try {
      await api.deleteConnection(id)
      load()
    } catch (e) {
      setError(e.message)
    }
  }

  return (
    <div className="connections-page">
      <h2>Database connections</h2>

      {error && <div className="error-banner">{error}</div>}

      <div className="connections-layout">
        {canEditConnections ? (
        <form className="connection-form" onSubmit={handleSave}>
          <h3>{editing ? 'Edit connection' : 'New connection'}</h3>

          <label>
            Display name
            <input value={form.name} onChange={(e) => update('name', e.target.value)} required
                   placeholder="Production reporting DB" />
          </label>

          <label>
            Database type
            <select value={form.providerType} onChange={(e) => update('providerType', e.target.value)}>
              <option value="SqlServer">SQL Server</option>
              <option value="Oracle">Oracle</option>
            </select>
          </label>

          <div className="form-row">
            <label className="grow">
              Host
              <input value={form.host} onChange={(e) => update('host', e.target.value)} required />
            </label>
            <label className="port-field">
              Port
              <input type="number" value={form.port} onChange={(e) => update('port', e.target.value)} />
            </label>
          </div>

          <label>
            {form.providerType === 'Oracle' ? 'Service name / SID' : 'Database'}
            <input value={form.database} onChange={(e) => update('database', e.target.value)}
                   placeholder={form.providerType === 'Oracle' ? 'XEPDB1' : 'ReportingDb'} />
          </label>

          <label>
            Username
            <input value={form.username} onChange={(e) => update('username', e.target.value)} autoComplete="off" />
          </label>

          <label>
            Password
            <input type="password" value={form.password} onChange={(e) => update('password', e.target.value)}
                   autoComplete="new-password"
                   placeholder={editing && form.hasPassword ? 'Leave blank to keep current password' : ''} />
          </label>
          <p className="field-hint hint-existing">
            Stored encrypted (AES-256) in connections.json and never sent back to the browser.
          </p>

          <label>
            Extra options (optional)
            <input value={form.extraOptions} onChange={(e) => update('extraOptions', e.target.value)}
                   placeholder="Encrypt=True;MultiSubnetFailover=True" />
          </label>

          <label>Who can use this connection</label>
          <UserAutocomplete
            multi
            value={form.allowedUsernames}
            onChange={(list) => { setForm((f) => ({ ...f, allowedUsernames: list })); setTestResult(null) }}
            placeholder="Type a username to restrict access…"
          />
          <p className="field-hint hint-existing">
            {form.allowedUsernames.length === 0
              ? 'Visible to everyone (default). Check specific users to restrict it to only them (Admins can always see every connection).'
              : `Restricted to: ${form.allowedUsernames.join(', ')} (plus Admins).`}
          </p>

          {testResult && (
            <div className={testResult.isReachable ? 'test-ok' : 'error-banner'}>
              {testResult.isReachable ? '✓ Connection succeeded' : `✗ ${testResult.error}`}
            </div>
          )}

          <div className="modal-actions">
            {editing && <button type="button" className="secondary" onClick={resetForm}>Cancel</button>}
            <button type="button" className="secondary" onClick={handleTest} disabled={testing || !form.host}>
              {testing ? 'Testing…' : 'Test connection'}
            </button>
            <button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
          </div>
        </form>
        ) : (
          <p className="muted">Viewers can see connections but need an Editor or Admin to add or change them.</p>
        )}

        <div className="connection-list">
          <h3>Saved connections</h3>
          {connections.length === 0 ? (
            <p className="muted">No connections yet. Add one using the form.</p>
          ) : (
            <ul className="favorite-list">
              {connections.map((c) => (
                <li key={c.id} className="favorite-card">
                  <div className="favorite-card-header">
                    <strong>{c.name}</strong>
                    <span className="badge">{c.providerType}</span>
                  </div>
                  <p className="muted connection-detail">
                    {c.username ? `${c.username}@` : ''}{c.host}{c.port ? `:${c.port}` : ''}
                    {c.database ? `/${c.database}` : ''}
                    {c.hasPassword ? ' · password saved' : ' · no password'}
                  </p>
                  {canEditConnections && (
                    <div className="favorite-card-actions">
                      <button className="secondary" onClick={() => startEdit(c)}>Edit</button>
                      <button className="secondary danger" onClick={() => handleDelete(c.id)}>Delete</button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  )
}
