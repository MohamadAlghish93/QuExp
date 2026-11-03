import CloudscapeInput from './CloudscapeInput.jsx'
import CloudscapeButton from './CloudscapeButton.jsx'
import { useState } from 'react'
import Alert from '@cloudscape-design/components/alert'
import Modal from '@cloudscape-design/components/modal'

// Shown before running a query that contains {{placeholder}} tokens - e.g. a
// favorite saved as "SELECT * FROM orders WHERE customer_id = {{customer_id}}".
// Values are sent to the API and bound as real DB parameters, never inserted
// into the SQL text on the client.
export default function ParameterModal({ paramNames, initialValues, onSubmit, onClose }) {
  const [values, setValues] = useState(() =>
    Object.fromEntries(paramNames.map((p) => [p, initialValues?.[p] ?? ''])))
  const [error, setError] = useState(null)

  function handleSubmit(e) {
    e.preventDefault()
    const missing = paramNames.filter((p) => !values[p]?.trim())
    if (missing.length > 0) {
      setError(`Fill in: ${missing.join(', ')}`)
      return
    }
    onSubmit(values)
  }

  return (
    <Modal
      visible
      size="medium"
      header="Fill in query parameters"
      onDismiss={onClose}
      closeAriaLabel="Close parameter dialog"
    >
      <form className="cloudscape-modal-content" onSubmit={handleSubmit}>
        <p className="muted">This query needs values for the placeholders below.</p>

        {paramNames.map((name) => (
          <label key={name}>
            {name}
            <CloudscapeInput
              value={values[name]}
              onChange={(e) => setValues((v) => ({ ...v, [name]: e.target.value }))}
              autoFocus={name === paramNames[0]}
            />
          </label>
        ))}

        {error && <Alert type="error">{error}</Alert>}

        <div className="modal-actions">
          <CloudscapeButton type="button" className="secondary" onClick={onClose}>Cancel</CloudscapeButton>
          <CloudscapeButton type="submit">Run query</CloudscapeButton>
        </div>
      </form>
    </Modal>
  )
}
