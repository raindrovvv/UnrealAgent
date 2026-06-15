# Clean Project Smoke Test

Use this checklist before tagging a release or announcing a build more broadly.

## Environment

- Windows development machine
- Unreal Engine 5.7 or compatible source build
- .NET 10 SDK/Runtime
- Python 3.8+ if testing the stdio MCP proxy
- At least one configured provider

## Checklist

1. Create or open a clean Unreal project.
2. Copy this repository to `Plugins/UnrealAgent`.
3. Enable the plugin in the `.uproject`.
4. Rebuild the Unreal project.
5. Launch Unreal Editor.
6. Open the UnrealAgent panel.
7. Confirm the Chat UI loads on `127.0.0.1:55558`.
8. Confirm the MCP endpoint responds on `127.0.0.1:55559/mcp`.
9. Send a short local quick reply prompt such as `안녕`.
10. Send a provider-backed prompt such as `너의 역할은 뭐야?`.
11. Attach a small PNG or JPEG image and verify the preview appears.
12. Run a read-only editor request such as output log summary or selected actor info.
13. Confirm a mutating tool request shows an appropriate permission flow.
14. Close Unreal Editor and confirm no unexpected runtime files need to be committed.

## Evidence To Capture

- Unreal Engine version
- Windows version
- Provider used
- Build result
- Chat UI screenshot
- One read-only tool result
- One permission dialog screenshot
- Any errors from Output Log
