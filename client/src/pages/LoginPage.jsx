import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import Alert from '@cloudscape-design/components/alert'
import Button from '@cloudscape-design/components/button'
import Container from '@cloudscape-design/components/container'
import Form from '@cloudscape-design/components/form'
import FormField from '@cloudscape-design/components/form-field'
import Header from '@cloudscape-design/components/header'
import Input from '@cloudscape-design/components/input'
import SpaceBetween from '@cloudscape-design/components/space-between'
import { useAuth } from '../auth/AuthContext.jsx'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await login(username, password)
      const redirectTo = location.state?.from || '/'
      navigate(redirectTo, { replace: true })
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="login-page">
      <Container header={<Header variant="h1">Query Explorer</Header>}>
        <form onSubmit={handleSubmit}>
          <Form
            actions={<Button variant="primary" formAction="submit" loading={loading} loadingText="Signing in">Sign in</Button>}
          >
            <SpaceBetween size="m">
              <p className="muted">Sign in to continue</p>
              <FormField label="Username">
                <Input
                  value={username}
                  onChange={({ detail }) => setUsername(detail.value)}
                  autoFocus
                  nativeInputAttributes={{ required: true }}
                />
              </FormField>
              <FormField label="Password">
                <Input
                  type="password"
                  value={password}
                  onChange={({ detail }) => setPassword(detail.value)}
                  nativeInputAttributes={{ required: true }}
                />
              </FormField>
              {error && <Alert type="error">{error}</Alert>}
              <p className="muted login-hint">
                First time? Default account: <code>admin</code> / <code>Admin123!</code>
              </p>
            </SpaceBetween>
          </Form>
        </form>
      </Container>
    </div>
  )
}
