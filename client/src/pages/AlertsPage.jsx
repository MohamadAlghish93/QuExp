import { useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { api } from '../api/client.js'

const EMPTY_FORM = {
  id: null,
  name: '',
  connectionId: '',
  sql: '',
  condition: 'RowCountGreaterThan',
  threshold: 0,
  checkIntervalMinutes: 15,
  webhookUrl: '',
  enabled: true,
}

const CONDITION_LABELS = {
  RowCountGreaterThan: 'returns more than',
  RowCountLessThan: 'returns fewer than',
  RowCountEquals: 'returns exactly',
}

export default function AlertsPage() {
  const location = useLocation()
  const [alerts, setAlerts] = useState([])
  const [connections, setConnections] = useState([])
  const [form, setForm] = useState(() => {
    const prefill = location.state || {}
    return {
      ...EMPTY_FORM,
      name: prefill.prefillName || '',
      sql: prefill.prefillSql || '',
      connectionId: prefill.prefillConnectionId || '',
    }
  })
  const [editing, setEditing] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testingId, setTestingId] = useState(null)
  const [testResult, setTestResult] = useState(null)
  const [error, setError] = useState(null)

  async function load() {
    try {
      const [a, c] = await Promise.all([api.getAlerts(), api.getConnections()])
      setAlerts(a)
      setConnections(c)
      if (!form.connectionId && c.length) setForm((f) => ({ ...f, connectionId: c[0].id }))
    } catch (e) {
      setError(e.message)
    }
  }

  useEffect(() => { load() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  function update(field, value) {
    setForm((f) => ({ ...f, [field]: value }))
  }

  function startEdit(alert) {
    setForm({
      id: alert.id,
      name: alert.name,
      connectionId: alert.connectionId,
      sql: alert.sql,
      condition: alert.condition,
      threshold: alert.threshold,
      checkIntervalMinutes: alert.checkIntervalMinutes,
      webhookUrl: alert.webhookUrl || '',
      enabled: alert.enabled,
    })
    setEditing(true)
  }

  function resetForm() {
    setForm({ ...EMPTY_FORM, connectionId: connections[0]?.id || '' })
    setEditing(false)
  }

  async function handleSave(e) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await api.saveAlert({
        ...form,
        threshold: Number(form.threshold) || 0,
        checkIntervalMinutes: Number(form.checkIntervalMinutes) || 15,
      })
      resetForm()
      load()
    } catch (e) {
      setError(e.message)
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(id) {
    if (!confirm('Delete this alert?')) return
    await api.deleteAlert(id)
    load()
  }

  async function handleTest(id) {
    setTestingId(id)
    setTestResult(null)
    try {
      const result = await api.testAlert(id)
      setTestResult({ id, ...result })
    } catch (e) {
      setTestResult({ id, error: e.message })
    } finally {
      setTestingId(null)
    }
  }

  return (
    <div className="connections-page">
      <h2>Alerts</h2>
      <p className="muted">
        Get notified when a saved query's row count crosses a threshold — e.g. "alert me if
        failed orders is more than 0". Checked automatically in the background on each alert's
        interval; notifications appear in the bell icon in the header.
      </p>

      {error && <div className="error-banner">{error}</div>}

      <div className="connections-layout">
        <form className="connection-form" onSubmit={handleSave}>
          <h3>{editing ? 'Edit alert' : 'New alert'}</h3>

          <label>
            Name
            <input value={form.name} onChange={(e) => update('name', e.target.value)} required
                   placeholder="Failed orders" />
          </label>

          <label>
            Connection
            <select value={form.connectionId} onChange={(e) => update('connectionId', e.target.value)} required>
              {connections.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </label>

          <label>
            SQL to check
            <textarea rows={3} value={form.sql} onChange={(e) => update('sql', e.target.value)} required
                      placeholder="SELECT * FROM orders WHERE status = 'failed'" />
          </label>

          <div className="form-row">
            <label className="grow">
              Alert when row count
              <select value={form.condition} onChange={(e) => update('condition', e.target.value)}>
                {Object.entries(CONDITION_LABELS).map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
            </label>
            <label className="port-field">
              Threshold
              <input type="number" value={form.threshold} onChange={(e) => update('threshold', e.target.value)} />
            </label>
          </div>

          <label>
            Check every (minutes)
            <input type="number" min="1" value={form.checkIntervalMinutes}
                   onChange={(e) => update('checkIntervalMinutes', e.target.value)} />
          </label>

          <label>
            Webhook URL (optional)
            <input value={form.webhookUrl} onChange={(e) => update('webhookUrl', e.target.value)}
                   placeholder="https://hooks.slack.com/services/…" />
          </label>
          <p className="field-hint hint-existing">
            If set, a JSON payload is POSTed here when the alert fires, in addition to the in-app notification.
          </p>

          <label className="checkbox-label">
            <input type="checkbox" checked={form.enabled} onChange={(e) => update('enabled', e.target.checked)} />
            Enabled
          </label>

          <div className="modal-actions">
            {editing && <button type="button" className="secondary" onClick={resetForm}>Cancel</button>}
            <button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save alert'}</button>
          </div>
        </form>

        <div className="connection-list">
          <h3>Your alerts</h3>
          {alerts.length === 0 ? (
            <p className="muted">No alerts yet.</p>
          ) : (
            <ul className="favorite-list">
              {alerts.map((a) => (
                <li key={a.id} className="favorite-card">
                  <div className="favorite-card-header">
                    <strong>{a.name}</strong>
                    <span className={a.enabled ? 'badge' : 'badge badge-muted'}>
                      {a.enabled ? 'enabled' : 'disabled'}
                    </span>
                  </div>
                  <p className="muted">
                    Alert when {CONDITION_LABELS[a.condition]} {a.threshold.toLocaleString()} rows,
                    checked every {a.checkIntervalMinutes} min, on <strong>{a.connectionName}</strong>
                  </p>
                  <pre className="favorite-sql">{a.sql}</pre>
                  <p className="muted history-meta">
                    {a.lastCheckedAtUtc
                      ? <>Last checked {new Date(a.lastCheckedAtUtc).toLocaleString()} · {a.lastRowCount ?? '?'} rows{a.lastError ? ` · error: ${a.lastError}` : ''}</>
                      : 'Not checked yet'}
                    {a.lastTriggeredAtUtc && <> · last triggered {new Date(a.lastTriggeredAtUtc).toLocaleString()}</>}
                  </p>
                  {testResult?.id === a.id && (
                    <p className={testResult.error ? 'error-banner' : 'test-ok'}>
                      {testResult.error
                        ? testResult.error
                        : `Test: ${testResult.rowCount.toLocaleString()} rows — condition ${testResult.conditionMet ? 'MET' : 'not met'}`}
                    </p>
                  )}
                  <div className="favorite-card-actions">
                    <button className="secondary" onClick={() => handleTest(a.id)} disabled={testingId === a.id}>
                      {testingId === a.id ? 'Testing…' : 'Test now'}
                    </button>
                    <button className="secondary" onClick={() => startEdit(a)}>Edit</button>
                    <button className="secondary danger" onClick={() => handleDelete(a.id)}>Delete</button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  )
}
