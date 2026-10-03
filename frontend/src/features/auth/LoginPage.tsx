import { Alert, Box, Button, CircularProgress, Container, Paper, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { ApiError } from '../../shared/api/apiError'
import { roleHomePath } from '../../app/rolePaths'
import { useAuth } from './AuthContext'

function loginError(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'invalid_credentials': return 'Kullanıcı adı veya parola hatalı.'
      case 'teacher_approval_pending': return 'Öğretmen hesabınız yönetici onayı bekliyor.'
      case 'teacher_account_rejected': return 'Öğretmen başvurunuz reddedilmiş.'
      case 'teacher_account_disabled': return 'Öğretmen hesabınız devre dışı.'
      case 'rate_limit_exceeded': return 'Çok fazla deneme yapıldı. Lütfen daha sonra tekrar deneyin.'
      default: return error.message
    }
  }
  return 'Giriş yapılamadı. Bağlantıyı kontrol edip yeniden deneyin.'
}

export function LoginPage() {
  const { state, login, retryRestore } = useAuth()
  const navigate = useNavigate()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (state.status === 'authenticated') return <Navigate to={roleHomePath(state.role)} replace />
  if (state.status === 'restoring') {
    return <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}><CircularProgress /></Box>
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      const role = await login(userName.trim(), password)
      navigate(roleHomePath(role), { replace: true })
    } catch (caught) {
      setError(loginError(caught))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Container maxWidth="sm" sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', py: 4 }}>
      <Paper elevation={3} sx={{ width: '100%', p: { xs: 3, sm: 5 } }}>
        <Stack spacing={3}>
          <Box>
            <Typography variant="h4" gutterBottom>EducationPlatform</Typography>
            <Typography color="text.secondary">Derslerine devam etmek için giriş yap.</Typography>
          </Box>
          {state.status === 'error' && (
            <Alert severity="warning" action={<Button color="inherit" onClick={() => void retryRestore()}>Yeniden dene</Button>}>
              Önceki oturum kontrol edilemedi. İstersen yeniden giriş yapabilirsin.
            </Alert>
          )}
          {error && <Alert severity="error">{error}</Alert>}
          <Box component="form" onSubmit={(event) => void handleSubmit(event)}>
            <Stack spacing={2.5}>
              <TextField
                label="Kullanıcı adı veya e-posta"
                value={userName}
                onChange={(event) => setUserName(event.target.value)}
                autoComplete="username"
                required
                fullWidth
              />
              <TextField
                label="Parola"
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="current-password"
                required
                fullWidth
              />
              <Button type="submit" variant="contained" size="large" disabled={submitting}>
                {submitting ? 'Giriş yapılıyor…' : 'Giriş yap'}
              </Button>
            </Stack>
          </Box>
        </Stack>
      </Paper>
    </Container>
  )
}
