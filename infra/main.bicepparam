using 'main.bicep'

// Values come from the environment (scripts/deploy-infra.sh loads .env), so nothing personal is committed.
param supabaseUrl = readEnvironmentVariable('PROD_SUPABASE_URL')
param webUrl = readEnvironmentVariable('PROD_WEB_URL', 'https://mitsuke.vercel.app/')
param bidRequestsTo = readEnvironmentVariable('BID_REQUESTS_TO')
param smtpUser = readEnvironmentVariable('SMTP_USER')
param adminPrincipalId = readEnvironmentVariable('ADMIN_PRINCIPAL_ID', '')
