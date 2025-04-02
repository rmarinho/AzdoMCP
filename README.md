# AzdoMCP

AzdoMCP is a .NET-based project designed to interact with Azure DevOps and other related services. It provides tools and services to streamline workflows and integrate with Azure DevOps APIs.

## Key Components

### AzdoMCP
- **AzdoTools.cs**: Contains utility functions for interacting with Azure DevOps.
- **Program.cs**: Entry point of the application.
- **appsettings.json**: Configuration file for the project.

### MCP.Services
- **AzdoService.cs**: Provides services for interacting with Azure DevOps APIs.
- **Models/AzdoModels.cs**: Contains data models used by the services.

## Prerequisites

- .NET 9.0 or later
- Azure DevOps Personal Access Token (PAT)
- VSCODE Insiders 
- Claude

## Configuration

The project uses environment variables for authentication:

- `AZDO_PAT`: Azure DevOps Personal Access Token

These can be configured in the `settings.json` file or passed as environment variables.

(Use inputs on VSCode Insiders for your PAT)
```
  "inputs": [
            {
                "type": "promptString",
                "id": "azdo-key",
                "password": true,
                "description": "AZDO PAT"
            }
        ],
```

## Running the Project

To run the project, use the following command:

```bash
        "azdoserver": {
                "type": "stdio",
                "command": "dotnet",
                "args": [
                    "run",
                    "--project",
                    "/Users/ruimarinho/AzdoMCP/AzdoMCP/AzdoMCP.csproj"
                ],
                "env": {
                    "AZDO_PAT": "${input:azdo-key}"
                }
            }
```
## License
This project is licensed under the MIT License. See the LICENSE file for details.

C## ontributing
Contributions are welcome! Please open an issue or submit a pull request for any changes or improvements.

## Contact
For any questions or support, please contact the repository maintainer.