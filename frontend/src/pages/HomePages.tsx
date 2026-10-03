import { Alert, CircularProgress, Paper, Stack, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { apiRequest } from '../shared/api/client'

function PlaceholderHome({ label, checkPath }: { label: string; checkPath: string }) {
  const [connection, setConnection] = useState<'checking' | 'ready' | 'failed'>('checking')

  useEffect(() => {
    let active = true
    void apiRequest<unknown[]>(checkPath)
      .then(() => { if (active) setConnection('ready') })
      .catch(() => { if (active) setConnection('failed') })
    return () => { active = false }
  }, [checkPath])

  return (
    <Paper sx={{ p: 4 }}>
      <Stack spacing={2}>
        <Typography variant="h5">{label} ana sayfası</Typography>
        <Typography color="text.secondary">
          Bu ilk adımda yalnızca oturum ve korumalı API bağlantısı doğrulanıyor. Özellik ekranları daha sonra eklenecek.
        </Typography>
        {connection === 'checking' && <Stack direction="row" sx={{ alignItems: 'center' }} spacing={2}><CircularProgress size={20} /><Typography>Bağlantı kontrol ediliyor…</Typography></Stack>}
        {connection === 'ready' && <Alert severity="success">Korumalı API isteği başarılı.</Alert>}
        {connection === 'failed' && <Alert severity="error">Korumalı API isteği başarısız. Bağlantıyı ve yetkiyi kontrol edin.</Alert>}
      </Stack>
    </Paper>
  )
}

export function AdminHomePage() {
  return <PlaceholderHome label="Yönetici" checkPath="/admin/teachers/pending" />
}

export function TeacherHomePage() {
  return <PlaceholderHome label="Öğretmen" checkPath="/classrooms" />
}

export function StudentHomePage() {
  return <PlaceholderHome label="Öğrenci" checkPath="/student/courses" />
}
