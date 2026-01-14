class PromptTemplates:
    PREFETCH_PREDICTION = """
    You are an AI optimization engine for a vector database.
    Given a sequence of user queries, predict the next likely semantic query description.
    
    Query History:
    {history}
    
    Predict the next query in JSON format: {{ "prediction": "description", "confidence": 0.0-1.0 }}
    """

    TTL_ADVICE = """
    Analyze the following cluster access patterns and recommend a TTL policy.
    
    Cluster ID: {cluster_id}
    Access Rate: {access_rate} ops/sec
    Last Update: {last_update} seconds ago
    
    Recommend JSON: {{ "action": "keep" | "shorten" | "evict", "ttl_seconds": <int> }}
    """

    CANONICAL_KEY = """
    Identify if the input query is a semantic alias of a known canonical key.
    
    Input: "{query}"
    Canonical Candidates:
    {candidates}
    
    Match JSON: {{ "canonical_id": <id> | null, "confidence": 0.0-1.0 }}
    """
