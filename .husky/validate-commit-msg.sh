#!/bin/bash

# Conventional Commits validation script
# Validates commit messages follow the format: type(scope): description

COMMIT_MSG_FILE=$1
COMMIT_MSG=$(cat "$COMMIT_MSG_FILE")

# Allow merge commits
if echo "$COMMIT_MSG" | grep -qE "^Merge "; then
    exit 0
fi

# Allow revert commits
if echo "$COMMIT_MSG" | grep -qE "^Revert "; then
    exit 0
fi

# Conventional commit pattern
# type(scope): description OR type: description
PATTERN="^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-zA-Z0-9_-]+\))?: .+"

if ! echo "$COMMIT_MSG" | grep -qE "$PATTERN"; then
    echo ""
    echo "ERROR: Invalid commit message format!"
    echo ""
    echo "Your message: $COMMIT_MSG"
    echo ""
    echo "Commit messages must follow Conventional Commits format:"
    echo "  type(scope): description"
    echo ""
    echo "Valid types:"
    echo "  feat     - A new feature"
    echo "  fix      - A bug fix"
    echo "  docs     - Documentation only changes"
    echo "  style    - Code style changes (formatting, semicolons, etc)"
    echo "  refactor - Code change that neither fixes a bug nor adds a feature"
    echo "  perf     - Performance improvement"
    echo "  test     - Adding or correcting tests"
    echo "  build    - Changes to build system or dependencies"
    echo "  ci       - Changes to CI configuration"
    echo "  chore    - Other changes that don't modify src or test files"
    echo "  revert   - Reverts a previous commit"
    echo ""
    echo "Examples:"
    echo "  feat(tools): add new assembly analysis tool"
    echo "  fix: resolve null reference in type loader"
    echo "  docs(readme): update installation instructions"
    echo ""
    exit 1
fi

exit 0
