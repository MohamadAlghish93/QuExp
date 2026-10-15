import { useState } from 'react'

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
    <div className="modal-backdrop" onClick={onClose}>
      <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSubmit}>
        <h3>Fill in query parameters</h3>
        <p className="muted">This query needs values for the placeholders below.</p>

        {paramNames.map((name) => (
          <label key={name}>
            {name}
            <input
              value={values[name]}
              onChange={(e) => setValues((v) => ({ ...v, [name]: e.target.value }))}
              autoFocus={name === paramNames[0]}
            />
          </label>
        ))}

        {error && <div className="error-banner">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary" onClick={onClose}>Cancel</button>
          <button type="submit">Run query</button>
        </div>
      </form>
    </div>
  )
}
