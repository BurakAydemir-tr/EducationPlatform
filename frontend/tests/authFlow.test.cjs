const assert = require('node:assert/strict')
const fs = require('node:fs')
const Module = require('node:module')
const path = require('node:path')
const { test } = require('node:test')
const typescript = require('typescript')

// Exercise the browser-independent auth modules with Node's test runner.
// TypeScript is already a build dependency; no extra test framework is needed.
require.extensions['.ts'] = (module, filename) => {
  const source = fs.readFileSync(filename, 'utf8')
  const output = typescript.transpileModule(source, {
    compilerOptions: { module: typescript.ModuleKind.CommonJS, target: typescript.ScriptTarget.ES2022 },
  }).outputText
  module._compile(output, filename)
}

const root = path.resolve(__dirname, '../src')
const configPath = path.join(root, 'shared/api/config.ts')
const configModule = new Module(configPath)
configModule.filename = configPath
configModule.loaded = true
configModule.exports = { apiBaseUrl: '/api' }
require.cache[configPath] = configModule

function loadAuthModules() {
  for (const file of [
    'features/auth/session.ts',
    'features/auth/authApi.ts',
    'shared/api/client.ts',
  ]) delete require.cache[path.join(root, file)]
  return {
    session: require(path.join(root, 'features/auth/session.ts')),
    authApi: require(path.join(root, 'features/auth/authApi.ts')),
    client: require(path.join(root, 'shared/api/client.ts')),
    roleHomePath: require(path.join(root, 'app/rolePaths.ts')).roleHomePath,
  }
}

function accessToken(userId, role, serial) {
  const claims = {
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': userId,
    'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': role,
    serial,
  }
  return `header.${Buffer.from(JSON.stringify(claims)).toString('base64url')}.signature`
}

function jsonResponse(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

test('login → role home → reload restore → protected request → one refresh/retry → logout', async () => {
  const values = new Map()
  global.sessionStorage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key),
  }

  const userId = 'a885c034-080f-4f18-92cd-f53f3f3e94ea'
  let tokenSerial = 0
  let activeRefreshToken = 'refresh-1'
  let currentAccessToken = accessToken(userId, 'Teacher', ++tokenSerial)
  let refreshCount = 0
  let protectedCount = 0
  let rejectedAccessRequests = 0
  let rejectWithForbidden = false
  let logoutCount = 0

  global.fetch = async (url, init = {}) => {
    if (url === '/api/auth/login') {
      assert.deepEqual(JSON.parse(init.body), { userName: 'teacher@example.com', password: 'password' })
      return jsonResponse({ accessToken: currentAccessToken, refreshToken: activeRefreshToken, accessTokenExpiresAt: new Date().toISOString() })
    }
    if (url === '/api/auth/refresh') {
      refreshCount++
      assert.equal(JSON.parse(init.body).refreshToken, activeRefreshToken)
      activeRefreshToken = `refresh-${refreshCount + 1}`
      currentAccessToken = accessToken(userId, 'Teacher', ++tokenSerial)
      return jsonResponse({ accessToken: currentAccessToken, refreshToken: activeRefreshToken, accessTokenExpiresAt: new Date().toISOString() })
    }
    if (url === '/api/classrooms') {
      protectedCount++
      if (rejectWithForbidden) return jsonResponse({ status: 403, code: 'forbidden', detail: 'Forbidden' }, 403)
      if (rejectedAccessRequests > 0) {
        rejectedAccessRequests--
        return jsonResponse({ status: 401, code: 'authentication_required' }, 401)
      }
      assert.equal(init.headers.Authorization, `Bearer ${currentAccessToken}`)
      return jsonResponse([])
    }
    if (url === '/api/auth/logout') {
      logoutCount++
      assert.equal(init.headers.Authorization, `Bearer ${currentAccessToken}`)
      assert.equal(JSON.parse(init.body).refreshToken, activeRefreshToken)
      return new Response(null, { status: 204 })
    }
    throw new Error(`Unexpected request: ${url}`)
  }

  let modules = loadAuthModules()
  assert.equal(modules.roleHomePath('Admin'), '/admin')
  assert.equal(modules.roleHomePath('Student'), '/student')
  const login = await modules.authApi.loginRequest('teacher@example.com', 'password')
  assert.equal(modules.roleHomePath(modules.session.saveSession(login).role), '/teacher')
  assert.equal(values.get('education-platform.refresh-token'), 'refresh-1')

  // Reload recreates the JavaScript module state, but preserves the tab's sessionStorage.
  modules = loadAuthModules()
  assert.equal(modules.session.getSession(), null)
  const restored = await modules.session.restoreSession()
  assert.equal(restored.role, 'Teacher')
  assert.equal(modules.roleHomePath(restored.role), '/teacher')
  assert.equal(refreshCount, 1)
  assert.deepEqual(await modules.client.apiRequest('/classrooms'), [])

  rejectedAccessRequests = 1
  assert.deepEqual(await modules.client.apiRequest('/classrooms'), [])
  assert.equal(refreshCount, 2)

  rejectedAccessRequests = 2
  const concurrentRefreshCount = refreshCount
  const concurrentRequests = await Promise.all([
    modules.client.apiRequest('/classrooms'),
    modules.client.apiRequest('/classrooms'),
  ])
  assert.deepEqual(concurrentRequests, [[], []])
  assert.equal(refreshCount, concurrentRefreshCount + 1, 'concurrent 401s share one refresh')

  const previousRefreshCount = refreshCount
  rejectWithForbidden = true
  await assert.rejects(() => modules.client.apiRequest('/classrooms'), { status: 403, code: 'forbidden' })
  assert.equal(refreshCount, previousRefreshCount, '403 must not trigger refresh')
  rejectWithForbidden = false

  await modules.session.logoutSession()
  assert.equal(logoutCount, 1)
  assert.equal(modules.session.getSession(), null)
  assert.equal(values.has('education-platform.refresh-token'), false)
  assert.equal(protectedCount, 8)
})
