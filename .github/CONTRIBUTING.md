# Contributing to Fontager

Thank you for your interest in contributing to Fontager! This document provides guidelines and information for contributors.

## Getting Started

### Prerequisites

- Windows 10 (19041+) or Windows 11
- Visual Studio 2022 (17.8+)
- .NET 8 SDK
- Windows App SDK workload for Visual Studio

### Setup

1. Fork the repository
2. Clone your fork locally
3. Open `Fontager.sln` in Visual Studio 2022
4. Set `Fontager.Viewer` as the startup project
5. Build and run to ensure everything works

## Development Guidelines

### Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods small and focused
- Use async/await for asynchronous operations

### Project Structure

```
Fontager/
├── Fontager.Core/          # Shared library
├── Fontager.Viewer/        # Font viewer application
├── Fontager.Manager/       # Font manager (planned)
└── docs/                   # Documentation
```

### Submitting Changes

1. Create a new branch for your feature/fix
2. Make your changes following the coding guidelines
3. Test your changes thoroughly
4. Update documentation if needed
5. Submit a pull request with a clear description

### Pull Request Process

- Use the provided pull request template
- Ensure all tests pass
- Update the CHANGELOG.md for significant changes
- Link any relevant issues in your PR description

## Testing

### Unit Tests

- Write unit tests for new functionality
- Use descriptive test names
- Test both success and failure scenarios

### Manual Testing

- Test with various font formats (TTF, OTF, TTC, WOFF2)
- Test on different Windows versions
- Test with large font collections
- Test edge cases (corrupted fonts, very large fonts, etc.)

## Bug Reports

When reporting bugs, please:

1. Use the bug report issue template
2. Provide detailed reproduction steps
3. Include environment information
4. Attach relevant font files if applicable
5. Add screenshots for UI issues

## Feature Requests

When requesting features:

1. Use the feature request issue template
2. Describe the problem you're trying to solve
3. Explain why this feature would be valuable
4. Consider implementation complexity

## Code of Conduct

Please be respectful and professional in all interactions. We want to maintain a welcoming environment for all contributors.

## Getting Help

If you need help:

- Check existing issues and discussions
- Create a question issue using the template
- Join our community discussions

## License

By contributing to Fontager, you agree that your contributions will be licensed under the same license as the project (MIT License).
