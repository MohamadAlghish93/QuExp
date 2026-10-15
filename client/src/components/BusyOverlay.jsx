// Full-screen translucent overlay with a spinner, shown while a slow action runs.
// Blocks every click behind it, so the action can't be triggered twice, and makes it
// obvious something is happening - the button label changing alone is easy to miss.
export default function BusyOverlay({ message = 'Working…' }) {
  return (
    <div className="busy-overlay" role="status" aria-live="polite">
      <div className="busy-overlay-box">
        <div className="spinner" />
        <span>{message}</span>
      </div>
    </div>
  )
}
