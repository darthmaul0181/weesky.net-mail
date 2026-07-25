import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { router } from './routes'
import { AuthProvider } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'

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

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>
  )
}
