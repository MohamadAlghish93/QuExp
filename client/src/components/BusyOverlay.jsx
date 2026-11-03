import Box from '@cloudscape-design/components/box'
import Modal from '@cloudscape-design/components/modal'
import Spinner from '@cloudscape-design/components/spinner'

// Full-screen translucent overlay with a spinner, shown while a slow action runs.
// Blocks every click behind it, so the action can't be triggered twice, and makes it
// obvious something is happening - the button label changing alone is easy to miss.
export default function BusyOverlay({ message = 'Working…' }) {
  return (
    <Modal visible size="small" header="Please wait">
      <Box textAlign="center" role="status" aria-live="polite">
        <Spinner size="large" />
        <Box padding={{ top: 's' }}>{message}</Box>
      </Box>
    </Modal>
  )
}
