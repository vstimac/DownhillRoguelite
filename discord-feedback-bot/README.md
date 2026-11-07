# Discord feedback bot

This bot listens only in the three configured feedback channels. A tester must react
with the matching marker emoji to submit a message:

- `🐛` in the bug channel
- `💡` in the suggestions channel
- `📝` in the general feedback channel

The bot creates one GitHub Issue per Discord message, includes the original message
URL and attachments, applies labels, reacts with `✅`, and replies with the Issue URL.

Requires Node.js 20+ and the Discord Message Content privileged intent.
