import { useState } from 'react'
import { Form, Input, Button, Checkbox, Alert } from 'antd'
import { MailOutlined, LockOutlined } from '@ant-design/icons'
import { api, setToken } from '../api.js'

export default function LoginPage({ onLogin }) {
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(values) {
    setError(null)
    setLoading(true)
    try {
      const data = await api.login(values.email, values.password)
      setToken(data.token, data.expiresIn, values.remember)
      onLogin()
    } catch {
      setError('Invalid credentials.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="page-center">
      <div className="card">
        {error && <Alert message={error} type="error" showIcon style={{ marginBottom: 16 }} />}
        <Form layout="vertical" onFinish={handleSubmit} initialValues={{ remember: false }}>
          <Form.Item name="email" style={{ marginBottom: 12 }}>
            <Input
              prefix={<MailOutlined />}
              type="email"
              autoComplete="email"
              placeholder="Email address"
              size="large"
            />
          </Form.Item>
          <Form.Item name="password" style={{ marginBottom: 12 }}>
            <Input.Password
              prefix={<LockOutlined />}
              autoComplete="current-password"
              placeholder="Password"
              size="large"
            />
          </Form.Item>
          <Form.Item name="remember" valuePropName="checked" style={{ marginBottom: 16 }}>
            <Checkbox>Remember me</Checkbox>
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={loading} block size="large">
            Sign in
          </Button>
        </Form>
      </div>
    </div>
  )
}
