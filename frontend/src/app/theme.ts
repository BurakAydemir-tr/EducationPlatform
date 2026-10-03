import { createTheme } from '@mui/material/styles'

export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#28467d' },
    secondary: { main: '#008c95' },
    background: { default: '#f3f6fa', paper: '#ffffff' },
  },
  shape: { borderRadius: 12 },
  typography: {
    fontFamily: 'Segoe UI, Arial, sans-serif',
    h4: { fontWeight: 700 },
    h5: { fontWeight: 700 },
  },
})
