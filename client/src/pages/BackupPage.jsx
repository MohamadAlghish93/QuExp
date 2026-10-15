import { useRef, useState } from 'react'
import { api } from '../api/client.js'

export default function BackupPage() {
  const [exporting, setExporting] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [message, setMessage] = useState(null)
  const [error, setError] = useState(null)
  const fileInputRef = useRef(null)

  async function handleExport() {
    setExporting(true)
    setError(null)
    setMessage(null)
    try {
      const bundle = await api.exportBackup()
      const blob = new Blob([JSON.stringify(bundle, null, 2)], { type: 'application/json' })
      const url = window.URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `query-explorer-backup-${new Date().toISOString().slice(0, 10)}.json`
      document.body.appendChild(a)
      a.click()
      a.remove()
      window.URL.revokeObjectURL(url)
      setMessage(`Exported ${bundle.connections.length} connections, ${bundle.favorites.length} favorites, ${bundle.alerts.length} alerts, ${bundle.users.length} users.`)
    } catch (e) {
      setError(e.message)
    } finally {
      setExporting(false)
    }
  }

  async function handleFileSelected(e) {
    const file = e.target.files?.[0]
    if (!file) return
    if (!confirm(
      'This will overwrite matching connections, favorites, alerts, and users with the data in this file. ' +
      'Connections come back with no password - you\'ll need to re-enter and test each one afterward. Continue?'
    )) {
      e.target.value = ''
      return
    }

    setRestoring(true)
    setError(null)
    setMessage(null)
    try {
      const text = await file.text()
      const bundle = JSON.parse(text)
      const result = await api.restoreBackup(bundle)
      setMessage(
        `Restored ${result.connectionsImported} connections, ${result.favoritesImported} favorites, ` +
        `${result.alertsImported} alerts, ${result.usersImported} users. ` +
        `Remember to re-enter passwords on the Connections page.`
      )
    } catch (err) {
      setError(err.message || 'That file could not be read as a valid backup.')
    } finally {
      setRestoring(false)
      e.target.value = ''
    }
  }

  return (
    <div className="favorites-page">
      <h2>Backup &amp; restore</h2>
      <p className="muted">
        Move data between environments, or take a manual backup. Connection passwords are never
        included - they're encrypted with this environment's key and wouldn't work anywhere else,
        so re-enter and test each connection after restoring.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {message && <p className="test-ok">{message}</p>}

      <div className="backup-actions">
        <div className="connection-form">
          <h3>Export</h3>
          <p className="muted">Downloads a single JSON file with connections (no passwords), favorites/templates, alerts, and users.</p>
          <button onClick={handleExport} disabled={exporting}>{exporting ? 'Exporting…' : 'Export all data'}</button>
        </div>

        <div className="connection-form">
          <h3>Restore</h3>
          <p className="muted">
            Imports a previously exported file. Existing records are matched by id/username and
            overwritten; nothing already present that isn't in the file gets deleted.
          </p>
          <input ref={fileInputRef} type="file" accept="application/json" onChange={handleFileSelected} disabled={restoring} />
        </div>
      </div>
    </div>
  )
}
