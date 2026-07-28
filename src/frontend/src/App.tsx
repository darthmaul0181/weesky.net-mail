import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { router } from './routes'
import { AuthProvider } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'
import { useWebAppManifest } from './hooks/useWebAppManifest'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A mailbox changes on the server without telling us, so refetch when the user returns.
      refetchOnWindowFocus: true,
      staleTime: 30_000,
      retry: (failureCount, error) =>
        // Never retry an auth failure: it will not succeed, and retrying delays the redirect
        // to /login behind two pointless round trips.
        (error as { status?: number })?.status === 401 ? false : failureCount < 2,
    },
  },
})

/** Renders nothing: it posts the <link rel="manifest">. Outside the router so it covers /login,
    the first page a new user sees — and so where installation is offered. */
function InstallManifest() {
  useWebAppManifest()
  return null
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <InstallManifest />
      <ThemeProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>
  )
}
