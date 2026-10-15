import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext.jsx'

// Like ProtectedRoute, but also requires the Admin role. Used for the Users page.
export default function AdminRoute({ children }) {
  const { isAuthenticated, isAdmin } = useAuth()
  if (!isAuthenticated) return <Navigate to="/login" replace />
  if (!isAdmin) return <Navigate to="/" replace />
  return children
}
