import { Box, Button, CircularProgress, Container, Stack, Typography } from '@mui/material'
import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'
import type { Role } from '../features/auth/session'
import { LoginPage } from '../features/auth/LoginPage'
import { AdminHomePage, StudentHomePage, TeacherHomePage } from '../pages/HomePages'
import { RoleLayout } from './RoleLayout'
import { roleHomePath } from './rolePaths'

function SessionLoading() {
  return (
    <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <Stack sx={{ alignItems: 'center' }} spacing={2}>
        <CircularProgress />
        <Typography>Oturum yükleniyor…</Typography>
      </Stack>
    </Box>
  )
}

function SessionError() {
  const { retryRestore } = useAuth()
  return (
    <Container maxWidth="sm" sx={{ py: 10, textAlign: 'center' }}>
      <Typography variant="h5" gutterBottom>Oturum kontrol edilemedi</Typography>
      <Typography color="text.secondary" sx={{ mb: 3 }}>
        Bağlantıyı kontrol edip yeniden deneyin.
      </Typography>
      <Button variant="contained" onClick={() => void retryRestore()}>Yeniden dene</Button>
    </Container>
  )
}

function RequireRole({ role }: { role: Role }) {
  const { state } = useAuth()
  if (state.status === 'restoring') return <SessionLoading />
  if (state.status === 'error') return <SessionError />
  if (state.status === 'anonymous') return <Navigate to="/login" replace />
  if (state.role !== role) return <Navigate to="/forbidden" replace />
  return <Outlet />
}

function HomeRedirect() {
  const { state } = useAuth()
  if (state.status === 'restoring') return <SessionLoading />
  if (state.status === 'error') return <SessionError />
  return <Navigate to={state.status === 'authenticated' ? roleHomePath(state.role) : '/login'} replace />
}

function MessagePage({ title, detail }: { title: string; detail: string }) {
  return (
    <Container maxWidth="sm" sx={{ py: 10, textAlign: 'center' }}>
      <Typography variant="h5" gutterBottom>{title}</Typography>
      <Typography color="text.secondary">{detail}</Typography>
      <Button href="/" sx={{ mt: 3 }}>Ana sayfaya dön</Button>
    </Container>
  )
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<HomeRedirect />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forbidden" element={<MessagePage title="Erişim izni yok" detail="Bu alan için yetkiniz bulunmuyor." />} />
      <Route element={<RequireRole role="Admin" />}>
        <Route path="/admin" element={<RoleLayout title="Yönetici alanı" />}>
          <Route index element={<AdminHomePage />} />
        </Route>
      </Route>
      <Route element={<RequireRole role="Teacher" />}>
        <Route path="/teacher" element={<RoleLayout title="Öğretmen alanı" />}>
          <Route index element={<TeacherHomePage />} />
        </Route>
      </Route>
      <Route element={<RequireRole role="Student" />}>
        <Route path="/student" element={<RoleLayout title="Öğrenci alanı" />}>
          <Route index element={<StudentHomePage />} />
        </Route>
      </Route>
      <Route path="*" element={<MessagePage title="Sayfa bulunamadı" detail="Bu adres için bir sayfa yok." />} />
    </Routes>
  )
}
