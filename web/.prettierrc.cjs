/** @type {import('prettier').Config} */
const config = {
  printWidth: 120,
  singleQuote: true,
  jsxSingleQuote: true,
  trailingComma: 'all',
  tabWidth: 2,
  semi: true,
  plugins: ['prettier-plugin-tailwindcss'],
};

module.exports = config;
