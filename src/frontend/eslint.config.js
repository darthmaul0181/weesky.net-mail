import js from '@eslint/js'
import globals from 'globals'
import react from 'eslint-plugin-react'
import reactHooks from 'eslint-plugin-react-hooks'
import jsxA11y from 'eslint-plugin-jsx-a11y'
import tseslint from 'typescript-eslint'

export default [
  { ignores: ['dist'] },
  {
    files: ['**/*.{js,jsx,ts,tsx}'],
    languageOptions: {
      ecmaVersion: 'latest',
      globals: globals.browser,
      parserOptions: {
        ecmaFeatures: { jsx: true },
      },
    },
    plugins: {
      react,
      'react-hooks': reactHooks,
    },
    rules: {
      ...js.configs.recommended.rules,
      ...react.configs.recommended.rules,
      ...reactHooks.configs.recommended.rules,
      'react/react-in-jsx-scope': 'off',
      'react/prop-types': 'off',
      'react-hooks/set-state-in-effect': 'error',
    },
    settings: {
      react: { version: 'detect' },
    },
  },
  {
    files: ['*.config.js'],
    languageOptions: { globals: globals.node },
  },
  {
    files: ['**/*.{jsx,tsx}'],
    plugins: {
      'jsx-a11y': jsxA11y,
    },
    rules: {
      ...jsxA11y.flatConfigs.recommended.rules,
      // jsx-a11y's static check cannot tell an ARIA `role` attribute from a same-named data prop
      // (EmptyFolderBanner's `role="trash"|"junk"|"archive"` folder role); ignoring non-DOM
      // components clears that false positive without silencing a real DOM `role` misuse.
      'jsx-a11y/aria-role': ['error', { ignoreNonDOM: true }],
      // `ComposeView` forwards `autoFocus` to `RecipientsField`'s own <input> (a genuine, named
      // exception below); ignoring non-DOM components stops the rule flagging the pass-through
      // prop itself, which carries no DOM `autoFocus` semantics of its own.
      'jsx-a11y/no-autofocus': ['error', { ignoreNonDOM: true }],
    },
  },
  ...tseslint.configs.recommendedTypeChecked.map(cfg => ({
    ...cfg,
    files: ['**/*.{ts,tsx}'],
  })),
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
  },
  {
    files: ['**/*.test.{ts,tsx}', 'src/test-utils.ts'],
    rules: {
      // `expect(obj.method)` hands the method over unbound on purpose: the rule's known false positive.
      '@typescript-eslint/unbound-method': 'off',
      // `act(async () => { … })` is async so that act drains the work it scheduled, and an async mock
      // keeps a thrown error a rejection like the function it stands in for: neither needs an await.
      '@typescript-eslint/require-await': 'off',
    },
  },
]
