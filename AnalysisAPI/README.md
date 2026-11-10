# AnalysisAPI

This is a standalone API project that hosts the dN/dS analysis endpoints for genome analysis using Galaxy and NCBI services.

## Overview

The AnalysisAPI project was extracted from the main CapstoneDesign project to provide better separation of concerns and allow the analysis functionality to run independently.

## Endpoints

All endpoints are prefixed with `/api/dnds`:

- `POST /api/dnds/genome-analysis/start` - Start genome-based analysis with OrthoFinder
- `GET /api/dnds/orthologous-groups/{historyId}/{orthoFinderJobId}` - Get orthologous groups from completed OrthoFinder run
- `POST /api/dnds/orthologous-group/analyze` - Analyze a single orthologous group
- `POST /api/dnds/orthologous-groups/analyze-multiple` - Analyze multiple orthologous groups
- `POST /api/dnds/orthologous-groups/analyze-all` - Analyze all orthologous groups
- `GET /api/dnds/job/{id}` - Get Galaxy job status
- `GET /api/dnds/verify-genome` - Verify NCBI has genome data for a species
- `GET /api/dnds/diag/toolbuild` - Diagnostics: verify Galaxy tool configuration
- `POST /api/dnds/diag/smoke` - Diagnostics: test Galaxy connectivity

## Running the API

### From the command line:

```bash
cd AnalysisAPI
dotnet run
```

The API will start on:
- HTTP: http://localhost:5040
- HTTPS: https://localhost:7040

### From Visual Studio:

1. Set AnalysisAPI as the startup project
2. Press F5 or click Run

## Configuration

Configuration is managed through `appsettings.json` and `appsettings.Development.json`.

Key configuration sections:

### Galaxy Configuration
```json
{
  "Galaxy": {
    "BaseUrl": "https://usegalaxy.eu/api",
    "ApiKey": "your-galaxy-api-key-here",
    "DnDsToolId": "toolshed.g2.bx.psu.edu/repos/iuc/hyphy_busted/hyphy_busted/2.5.47+galaxy0",
    "UnzipToolId": "toolshed.g2.bx.psu.edu/repos/imgteam/unzip/unzip/6.0+galaxy2"
  }
}
```

### NCBI Configuration
```json
{
  "Ncbi": {
    "BaseUrl": "https://api.ncbi.nlm.nih.gov",
    "RequestsPerSecond": 3
  }
}
```

## Architecture

The AnalysisAPI project:
- References the CapstoneServices project which contains Galaxy and NCBI clients
- Hosts only the DnDs controller for analysis operations
- Runs independently on port 5040 (HTTP) / 7040 (HTTPS)
- Uses CORS to allow requests from the React frontend (localhost:3000)

## Frontend Configuration

If you're using the React frontend, update the API base URL in your frontend configuration to point to:
- Development: `http://localhost:5040/api/dnds`

The frontend code in `ClientApp/src/api/dndsApi.js` should work without changes since the route paths remain the same.

## Dependencies

The AnalysisAPI depends on:
- .NET 8.0
- CapstoneServices project (contains Galaxy and NCBI clients)
- Galaxy Europe API (https://usegalaxy.eu)
- NCBI Datasets API (https://api.ncbi.nlm.nih.gov)

## Development Notes

- The controller uses camelCase for JSON serialization to match JavaScript conventions
- All routes maintain the same paths as before (`/api/dnds/*`)
- CORS is configured to allow requests from localhost:3000 (React dev server)
- Logging is configured with debug-level output for Galaxy and NCBI services
