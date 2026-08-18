import sys
import pandas as pd
import os

excel_path = sys.argv[1]
csv_path = "output.csv"

# Get file extension
ext = os.path.splitext(excel_path)[1].lower()

# Choose engine based on extension
if ext == ".xlsx" or ext == ".xlsm":
    engine = "openpyxl"
elif ext == ".xls":
    engine = "xlrd"
else:
    raise ValueError(f"Unsupported file extension: {ext}")

# Read and convert
df = pd.read_excel(excel_path, engine=engine)
df.to_csv(csv_path, index=False)
