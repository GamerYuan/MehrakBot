import json
import sys
from typing import List, Dict, Any

def load_json_file(file_path: str) -> Dict[str, Any]:
    """Load and parse the JSON file."""
    try:
        # First try with utf-8-sig to handle BOM
        with open(file_path, 'r', encoding='utf-8-sig') as file:
            return json.load(file)
    except FileNotFoundError:
        print(f"Error: File '{file_path}' not found.", file=sys.stderr)
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON format - {e}", file=sys.stderr)
        sys.exit(1)
    except UnicodeDecodeError:
        # Fallback to regular utf-8 if utf-8-sig fails
        try:
            with open(file_path, 'r', encoding='utf-8') as file:
                return json.load(file)
        except Exception as e:
            print(f"Error: Could not read file - {e}", file=sys.stderr)
            sys.exit(1)

def generate_markdown_table(data: Dict[str, Any]) -> str:
    """Generate a markdown table from the JSON data."""
    # Extract the game name and aliases
    game = data.get('game', 'Unknown Game')
    aliases = data.get('aliases', [])
    
    if not aliases:
        return f"# {game}\n\nNo aliases found."
    
    # Start building the markdown table
    markdown = f"# {game}\n\n"
    markdown += "| Name | Aliases |\n"
    markdown += "|------|--------|\n"
    
    # Process each alias entry
    for entry in aliases:
        name = entry.get('name', '')
        alias_list = entry.get('alias', [])
        
        # Join aliases with commas
        aliases_str = ', '.join(alias_list) if alias_list else ''
        
        # Escape any pipe characters in the content to avoid breaking the table
        name_escaped = name.replace('|', '\\|')
        aliases_escaped = aliases_str.replace('|', '\\|')
        
        markdown += f"| {name_escaped} | {aliases_escaped} |\n"
    
    return markdown

def main():
    """Main function to handle command line arguments and process the file."""
    if len(sys.argv) != 2:
        print("Usage: python alias_to_markdown.py <input_json_file>", file=sys.stderr)
        print("Example: python alias_to_markdown.py aliases.json", file=sys.stderr)
        sys.exit(1)
    
    input_file = sys.argv[1]
    
    # Load the JSON data
    data = load_json_file(input_file)
    
    # Generate and output the markdown table to stdout
    markdown_content = generate_markdown_table(data)
    print(markdown_content)

if __name__ == "__main__":
    main()