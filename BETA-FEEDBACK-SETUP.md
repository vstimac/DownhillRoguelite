# The Descent beta feedback

## Discord bot

Run the bot in `discord-feedback-bot/` as a continuously running Node process. Copy `.env.example` to `.env`, set the bot token and channel IDs, and give the GitHub token Issues read/write access only to this repository. Enable Message Content Intent and give the bot permissions to view the beta channels, read their history, react and reply. Install dependencies with `npm install`, then run `npm start`.

The GitHub repository is public. Tell testers in the designated channels that reacting to **their own** message with the channel's feedback emoji publishes its text, attachments and original link in a public GitHub Issue. Use 🐛 for bugs, 💡 for feature requests and 📝 for general feedback. The bot adds `beta-feedback` and the type label.

## GitHub approval and runner

Merge the workflow after review. Configure the Bedrock OIDC role for this repository's default branch, with only the needed model invocation permissions. Set the Actions secret `AWS_ROLE_TO_ASSUME` and Actions variables `AWS_REGION` and `CLAUDE_BEDROCK_MODEL`. Use a protected default branch with required human PR review. Claude Code proposes a PR on a separate branch.

A maintainer with write access approves a specific open, unlocked issue by adding `ai-approved` on GitHub. The workflow verifies the approver and re-reads the current issue status and labels before using Bedrock credentials. An approval label already present before the workflow is installed does not emit a new event.

## Verification

Submit a harmless report through a test channel, verify the two labels and the Discord link, then add `ai-approved` on the GitHub issue. Inspect the associated Actions run and the proposed PR when applicable. The Site uses the run title and the issue reference in the PR body to connect the evidence.
