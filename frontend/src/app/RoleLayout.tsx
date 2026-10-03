import { Alert, AppBar, Box, Button, Container, Stack, Toolbar, Typography } from '@mui/material'
import { useState } from 'react'
import { Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'

export function RoleLayout({ title }: { title: string }) {
  const { logout } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleLogout(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await logout()
      navigate('/login', { replace: true })
    } catch {
      setError('Çıkış sunucuda doğrulanamadı. Bağlantıyı kontrol edip yeniden deneyin.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Box sx={{ minHeight: '100vh' }}>
      <AppBar position="static" elevation={0}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            EducationPlatform
          </Typography>
          <Button color="inherit" onClick={() => void handleLogout()} disabled={busy}>
            {busy ? 'Çıkılıyor…' : 'Çıkış yap'}
          </Button>
        </Toolbar>
      </AppBar>
      <Container maxWidth="md" sx={{ py: 5 }}>
        <Stack spacing={3}>
          <Typography variant="h4">{title}</Typography>
          {error && <Alert severity="error">{error}</Alert>}
          <Outlet />
        </Stack>
      </Container>
    </Box>
  )
}
