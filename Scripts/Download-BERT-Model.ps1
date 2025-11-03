# Download BERT Model for Offline Use

Write-Host "Downloading BERT model (all-MiniLM-L6-v2) for offline semantic embeddings..." -ForegroundColor Green

# Create directory
$modelDir = "d:\tinyllama\models\all-MiniLM-L6-v2"
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

# Download model.onnx (90 MB)
$modelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx"
$modelPath = "$modelDir\model.onnx"

Write-Host "Downloading model.onnx (90 MB)..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $modelUrl -OutFile $modelPath

Write-Host ""
Write-Host "? Download complete!" -ForegroundColor Green
Write-Host "   Model location: $modelPath"
Write-Host "   Size: $((Get-Item $modelPath).Length / 1MB) MB"
Write-Host ""
Write-Host "You now have REAL BERT embeddings!" -ForegroundColor Cyan
Write-Host "  - 'hello' will NOT match 'monster cards'" -ForegroundColor Cyan
Write-Host "  - 'fight' WILL match 'combat' (semantic understanding)" -ForegroundColor Cyan
