import { useEffect, useState } from 'react'
import Alert from '@cloudscape-design/components/alert'
import Box from '@cloudscape-design/components/box'
import Header from '@cloudscape-design/components/header'
import StatusIndicator from '@cloudscape-design/components/status-indicator'
import Table from '@cloudscape-design/components/table'
import { api } from '../api/client.js'

export default function AuditLogPage() {
  const [entries, setEntries] = useState([])
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    api.getAuditLog(200, 0)
      .then(setEntries)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="favorites-page">
      {error && <Alert type="error">{error}</Alert>}
      <Table
        columnDefinitions={[
          { id: 'when', header: 'When', cell: (entry) => new Date(entry.createdAtUtc).toLocaleString() },
          { id: 'who', header: 'Who', cell: (entry) => `@${entry.actorUsername}` },
          { id: 'action', header: 'Action', cell: (entry) => <StatusIndicator type="info">{entry.action}</StatusIndicator> },
          { id: 'details', header: 'Details', cell: (entry) => entry.targetDescription },
        ]}
        items={entries}
        trackBy="id"
        loading={loading}
        loadingText="Loading audit log"
        empty={<Box textAlign="center">No admin actions logged yet.</Box>}
        header={<Header variant="h2" description="Admin-level actions — who did what, and when.">Audit log</Header>}
      />
    </div>
  )
}
