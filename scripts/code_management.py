import sys
import pymongo
from pymongo import MongoClient

def main():
    if len(sys.argv) != 4:
        print("Usage: python3 code_management.py [add|delete] [game] [code]")
        print("Example: python3 code_management.py add Genshin TESTCODE")
        sys.exit(1)
    
    operation = sys.argv[1].lower()
    game = sys.argv[2]
    code = sys.argv[3].upper()  # Convert to uppercase
    
    if operation not in ['add', 'delete']:
        print("Error: Operation must be 'add' or 'delete'")
        sys.exit(1)
    
    try:
        # Connect to MongoDB
        client = MongoClient("mongodb://localhost:27017/")  # Adjust connection string as needed
        db = client["MehrakBot"]
        collection = db["codes"]
        
        # Find existing document for the game
        document = collection.find_one({"game": game})
        
        if operation == "add":
            if document is None:
                # Create new document
                new_doc = {
                    "game": game,
                    "codes": [code]
                }
                collection.insert_one(new_doc)
                print(f"Created new entry for {game} and added code: {code}")
            else:
                # Check if code already exists
                if document.get("codes") and code in document["codes"]:
                    print(f"Code '{code}' already exists for {game}")
                else:
                    # Add code to existing document
                    collection.update_one(
                        {"game": game},
                        {"$addToSet": {"codes": code}}
                    )
                    print(f"Added code '{code}' to {game}")
        
        elif operation == "delete":
            if document is None or not document.get("codes") or code not in document["codes"]:
                print(f"Code '{code}' not found for {game}")
            else:
                # Remove code from document
                collection.update_one(
                    {"game": game},
                    {"$pull": {"codes": code}}
                )
                print(f"Removed code '{code}' from {game}")
                
                # Check if codes array is now empty and optionally remove the document
                updated_doc = collection.find_one({"game": game})
                if not updated_doc.get("codes"):
                    collection.delete_one({"game": game})
                    print(f"Removed empty entry for {game}")
    
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)
    
    finally:
        if 'client' in locals():
            client.close()

if __name__ == "__main__":
    main()