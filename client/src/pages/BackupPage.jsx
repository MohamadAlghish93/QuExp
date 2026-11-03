import CloudscapeButton from '../components/CloudscapeButton.jsx'
import Alert from '@cloudscape-design/components/alert'
import FileUpload from '@cloudscape-design/components/file-upload'
import { useState } from 'react'
import { api } from '../api/client.js'

export default function BackupPage() {
  const [exporting, setExporting] = useState(false)
  const [restoring, setRestoring] = useState(false)
  const [message, setMessage] = useState(null)
  const [error, setError] = useState(null)
  const [backupFiles, setBackupFiles] = useState([])

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

  async function handleFileSelected(file) {
    if (!file) return
    if (!confirm(
      'This will overwrite matching connections, favorites, alerts, and users with the data in this file. ' +
      'Connections come back with no password - you\'ll need to re-enter and test each one afterward. Continue?'
    )) {
      setBackupFiles([])
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
      setBackupFiles([])
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

      {error && <Alert type="error">{error}</Alert>}
      {message && <p className="test-ok">{message}</p>}

      <div className="backup-actions">
        <div className="connection-form">
          <h3>Export</h3>
          <p className="muted">Downloads a single JSON file with connections (no passwords), favorites/templates, alerts, and users.</p>
          <CloudscapeButton onClick={handleExport} disabled={exporting}>{exporting ? 'Exporting…' : 'Export all data'}</CloudscapeButton>
        </div>

        <div className="connection-form">
          <h3>Restore</h3>
          <p className="muted">
            Imports a previously exported file. Existing records are matched by id/username and
            overwritten; nothing already present that isn't in the file gets deleted.
          </p>
          <FileUpload
            accept=".json,application/json"
            value={backupFiles}
            disabled={restoring}
            onChange={({ detail }) => {
              setBackupFiles(detail.value)
              if (detail.value[0]) handleFileSelected(detail.value[0])
            }}
            i18nStrings={{
              uploadButtonText: (multiple) => multiple ? 'Choose backup files' : 'Choose backup file',
              dropzoneText: (multiple) => multiple ? 'Drop backup files to upload' : 'Drop a backup file to upload',
              removeFileAriaLabel: (index) => `Remove file ${index + 1}`,
              errorIconAriaLabel: 'Error',
            }}
          />
        </div>
      </div>
    </div>
  )
}
