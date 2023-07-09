# Define counters
$passedCount = 0
$failedCount = 0
$flakyTests = @{}

# Run dotnet test 100 times
for ($i = 0; $i -lt 500; $i++)
{
    # Run dotnet test and capture the output
    $output = dotnet test -v n | Out-string

    # Check if tests passed or failed
    if ($output -like '*Test Run Successful.*')
    {
        $passedCount++
        Write-Host "Run Number $i was succesful"
    }
    else
    {
        Write-Host "Run Number $i had a failure"
        Write-Host $output

        $failedCount++
        # Find the failed tests
        $failedTests = $output | Select-String -Pattern "(?<=Failed\s+)[\w\s.]+(?=\s+\[)"
           foreach ($test in $failedTests) {
                Write-Host $test

            if ($flakyTests.ContainsKey($test)) {
                $flakyTests[$test]++
            }
            else {
                $flakyTests[$test] = 1
            }
        }
    }
}

# Output the results
Write-Host "Passed: $passedCount"
Write-Host "Failed: $failedCount"
Write-Host "Flaky tests: "
$flakyTests.GetEnumerator() | Sort-Object Value -Descending | Format-Table -AutoSize
