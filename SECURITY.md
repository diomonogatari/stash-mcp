# Security Policy

## Credential Safety

**Never commit your Bitbucket Personal Access Token (PAT) to source control.**

- Pass credentials through environment variables (`BITBUCKET_URL`,
  `BITBUCKET_TOKEN`) as shown in the [README](README.md)
- Restrict PAT permissions to the minimum required scopes
- Use `BITBUCKET_READ_ONLY_MODE=true` when write access is not needed
- Use secure credential storage where available

## Docker Image

The published Docker image on
[Docker Hub](https://hub.docker.com/r/diomonogatari/stash-mcp)
uses the official Microsoft .NET runtime base image and is scanned for
vulnerabilities via Docker Scout static scanning on every push.
