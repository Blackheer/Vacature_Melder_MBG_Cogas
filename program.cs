name: Check Cogas Vacancies (Playwright)

on:
  schedule:
    - cron: '0 8 * * *'
  workflow_dispatch:

jobs:
  check-jobs:
    runs-on: ubuntu-latest
    permissions:
      contents: write
      
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Set up .NET Core
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build project
        run: dotnet build --configuration Release

      - name: Install Playwright browsers
        run: pwsh bin/Release/net8.0/playwright.ps1 install --with-deps

      - name: Run C# script
        env:
          MAIL_USERNAME: ${{ secrets.MAIL_USERNAME }}
          MAIL_PASSWORD: ${{ secrets.MAIL_PASSWORD }}
          MAIL_TO: ${{ secrets.MAIL_TO }}
        run: dotnet run --configuration Release --no-build

      - name: Commit and push if vacancies changed
        run: |
          git config --global user.name "GitHub Action"
          git config --global user.email "action@github.com"
          git add vacancies.json
          git diff --staged --quiet || git commit -m "Update vacatures lijst via Playwright"
          git push
