# Deployment Guide

## 1. Install the tools

```bash
dotnet tool install -g Amazon.Lambda.Tools
```

Install the [AWS SAM CLI](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/install-sam-cli.html).

## 2. Create a Gmail app password

1. Open your Google Account.
2. Go to **Security**.
3. Enable **2-Step Verification**.
4. Open **App passwords**.
5. Create a password and copy the generated 16-character value.

## 3. Deploy with SAM

For the first deployment, run:

```bash
sam build
sam deploy --guided
```

Use the following prompts:

| Parameter | Example value |
| --- | --- |
| Stack name | `job-search-stack` |
| Region | `eu-west-1` |
| GmailSmtpUser | `your@gmail.com` |
| GmailSmtpPassword | `xxxx xxxx xxxx xxxx` |
| RecipientEmail | `your@gmail.com` |

## 4. Configure GitHub Secrets

Configure the following repository secrets for CI/CD deployment on pushes to `main`:

| Secret | Value |
| --- | --- |
| `AWS_ACCESS_KEY_ID` | From **IAM → Users → Security credentials** |
| `AWS_SECRET_ACCESS_KEY` | From the same IAM user |

## 5. Test the Lambda function manually

```bash
aws lambda invoke \
  --function-name job-search-daily \
  --payload '{}' \
  response.json

cat response.json
```