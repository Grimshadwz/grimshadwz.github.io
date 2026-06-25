import json

# Reads your clean local asset array
with open("inventory.json", "r") as f:
    products = json.load(f)

# Builds the master brand introduction headers
markdown = """# Ratchet Art Studio | Hand-Crafted Premium 3D Assets

[...Intro text...]

## 🛠 Premium 3D Asset Inventory

"""

# Dynamically loops through your items and formats them
for i, item in enumerate(products, 1):
    markdown += f"### {i}. {item['title']}\n\n"
    markdown += f"{item['specs']}\n\n"
    markdown += f"* **Direct Shop Link**: [{item['title']}]({item['link']})\n\n"
    markdown += "**************************************************\n\n"

# Writes the finalized error-free document
with open("README.md", "w") as f:
    f.write(markdown)
