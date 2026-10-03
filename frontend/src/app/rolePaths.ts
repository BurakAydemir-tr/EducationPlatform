import type { Role } from '../features/auth/session'

export function roleHomePath(role: Role): string {
  switch (role) {
    case 'Admin': return '/admin'
    case 'Teacher': return '/teacher'
    case 'Student': return '/student'
  }
}
