# Gather on Amazon EC2

The portfolio demo runs on one Sydney (`ap-southeast-2`) EC2 instance. The instance needs Docker Engine and Compose v2. Use a Free Tier eligible Linux AMI, a `t3.small` with Standard CPU credits, and a 20 GiB encrypted gp3 volume. Allow inbound TCP 80 and 443 for HTTPS and certificate validation. Restrict port 22 to the Sydney EC2 Instance Connect service. Keep ports 5432 and 8080 closed to the internet.

## Configure and start

Associate an Elastic IP before configuring HTTPS. From the directory containing the deployment release, create a private environment file.

```bash
umask 077
cp -n deploy/aws.env.example .env.aws
chmod 600 .env.aws
```

Set `GATHER_PUBLIC_HOST` to the Elastic IP without a scheme or port. Use a fresh hex value for `POSTGRES_PASSWORD` so it can be embedded safely in the connection string. Use a separate, strong `DEMO_ACCOUNT_PASSWORD` for the private organiser and check-in accounts. Never reuse a local database volume or copy local credentials into the release. Start with the Stripe and OpenAI settings empty for the first smoke test.

```bash
docker compose --env-file .env.aws -f compose.production.yaml config --quiet
docker compose --env-file .env.aws -f compose.production.yaml build app
GATHER_INITIALIZE=true docker compose --env-file .env.aws -f compose.production.yaml up -d
```

This initializes the database and seeds the demo on the first release. Wait for `https://<elastic-ip>/api/health` to return `{"status":"healthy"}` with a trusted certificate. Then run `docker compose --env-file .env.aws -f compose.production.yaml up -d` without `GATHER_INITIALIZE` to recreate the app without startup migrations. Set `GATHER_IMAGE` to a release tag or digest when using a prebuilt application image.

The certificate is valid for about six days. Caddy renews it using the persisted `certificates` volume. Verify HTTPS and certificate renewal before treating the URL as a long-lived portfolio link. Check sign-in, registration, free booking, ticket check-in, and the rejection of a second scan. Refresh the fictional event dates through the organiser UI when they pass. For Stripe, register a separate sandbox webhook for the public HTTPS URL and install its signing secret in `.env.aws`. Use only a sandbox API key. Configure OpenAI separately after the smoke test.

## Back up and take down

```bash
umask 077
docker compose --env-file .env.aws -f compose.production.yaml exec -T postgres \
  pg_dump -U gather -d gather -Fc > gather-demo.dump
```

Save the dump privately off the instance and verify it by restoring into a separate disposable database. Preserve the app image tag so you can redeploy the same release. The Compose volumes retain PostgreSQL, cookie signing keys, and certificate state when the containers stop. Stopping EC2 preserves the disk and the allocated Elastic IP, both of which continue to count against the credits. At the end of the test window, after verifying the backup, terminate the test instance and release the Elastic IP. Check the console for any remaining volumes, snapshots, or other metered resources.
