/**
 * OfflineAI API JavaScript Client
 * 
 * A comprehensive JavaScript client for interacting with the OfflineAI REST API.
 * Includes error handling, timeout management, and RAG support.
 * 
 * @version 1.0.0
 * @author OfflineAI Team
 */

class OfflineAIClient {
    /**
     * Create a new OfflineAI client.
     * @param {string} baseUrl - The base URL of the API (default: http://localhost:5000)
     * @param {number} timeoutMs - Default timeout in milliseconds (default: 35000)
     */
    constructor(baseUrl = 'http://localhost:5000', timeoutMs = 35000) {
        this.baseUrl = baseUrl.replace(/\/$/, ''); // Remove trailing slash
        this.defaultTimeout = timeoutMs;
    }

    /**
     * Query the LLM with optional RAG support.
     * @param {Object} options - Query options
     * @param {string} options.question - The question to ask (required)
     * @param {boolean} [options.enableRag=true] - Enable RAG retrieval
     * @param {number} [options.maxTokens=512] - Maximum tokens to generate
     * @param {number} [options.temperature=0.3] - Temperature for generation (0.0-2.0)
     * @param {number} [options.topK=3] - Number of documents to retrieve for RAG
     * @param {number} [options.minRelevanceScore=0.5] - Minimum relevance score for RAG
     * @param {string} [options.model] - Model to use (optional)
     * @param {string} [options.context] - Pre-provided context (optional)
     * @param {number} [options.timeout] - Custom timeout in ms (optional)
     * @returns {Promise<Object>} Query response
     */
    async query(options) {
        if (!options.question) {
            throw new Error('Question is required');
        }

        const request = {
            question: options.question,
            enableRag: options.enableRag !== undefined ? options.enableRag : true,
            maxTokens: options.maxTokens || 512,
            temperature: options.temperature !== undefined ? options.temperature : 0.3,
            topK: options.topK || 3,
            minRelevanceScore: options.minRelevanceScore !== undefined ? options.minRelevanceScore : 0.5,
            ...(options.model && { model: options.model }),
            ...(options.context && { context: options.context })
        };

        return this._fetchWithTimeout(
            `${this.baseUrl}/api/query`,
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            },
            options.timeout || this.defaultTimeout
        );
    }

    /**
     * Ask a simple question without RAG.
     * @param {string} question - The question to ask
     * @param {number} maxTokens - Maximum tokens (default: 256)
     * @returns {Promise<string>} The answer
     */
    async ask(question, maxTokens = 256) {
        const response = await this.query({
            question,
            enableRag: false,
            maxTokens
        });
        return response.answer;
    }

    /**
     * Ask a question with RAG support.
     * @param {string} question - The question to ask
     * @param {number} topK - Number of documents to retrieve (default: 3)
     * @returns {Promise<string>} The answer
     */
    async askWithRAG(question, topK = 3) {
        const response = await this.query({
            question,
            enableRag: true,
            topK
        });
        return response.answer;
    }

    /**
     * Validate a query request without executing it.
     * @param {Object} request - The query request to validate
     * @returns {Promise<Object>} Validation result
     */
    async validate(request) {
        return this._fetchWithTimeout(
            `${this.baseUrl}/api/query/validate`,
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            }
        );
    }

    /**
     * Check API health status.
     * @returns {Promise<Object>} Health status
     */
    async health() {
        return this._fetch(`${this.baseUrl}/api/health`);
    }

    /**
     * Get list of available models.
     * @returns {Promise<Array>} List of models
     */
    async getModels() {
        return this._fetch(`${this.baseUrl}/api/health/models`);
    }

    /**
     * Fetch with timeout support.
     * @private
     */
    async _fetchWithTimeout(url, options, timeoutMs = this.defaultTimeout) {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

        try {
            const response = await fetch(url, {
                ...options,
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                const errorData = await response.json();
                throw new OfflineAIError(
                    errorData.error,
                    response.status,
                    errorData.details,
                    errorData.suggestions
                );
            }

            return await response.json();
        } catch (error) {
            clearTimeout(timeoutId);

            if (error.name === 'AbortError') {
                throw new OfflineAIError(
                    'Request timed out',
                    408,
                    `Request exceeded ${timeoutMs}ms timeout`,
                    ['Try a shorter question', 'Reduce maxTokens', 'Disable RAG']
                );
            }

            throw error;
        }
    }

    /**
     * Simple fetch wrapper.
     * @private
     */
    async _fetch(url) {
        const response = await fetch(url);
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        return response.json();
    }
}

/**
 * Custom error class for OfflineAI API errors.
 */
class OfflineAIError extends Error {
    constructor(message, statusCode, details, suggestions = []) {
        super(message);
        this.name = 'OfflineAIError';
        this.statusCode = statusCode;
        this.details = details;
        this.suggestions = suggestions;
    }
}

// Export for use in different environments
if (typeof module !== 'undefined' && module.exports) {
    // Node.js
    module.exports = { OfflineAIClient, OfflineAIError };
} else {
    // Browser
    window.OfflineAIClient = OfflineAIClient;
    window.OfflineAIError = OfflineAIError;
}

// ============================================================================
// USAGE EXAMPLES
// ============================================================================

/**
 * Example 1: Simple question
 */
async function example1_SimpleQuestion() {
    const client = new OfflineAIClient();
    
    try {
        const answer = await client.ask("What is machine learning?");
        console.log("Answer:", answer);
    } catch (error) {
        console.error("Error:", error.message);
    }
}

/**
 * Example 2: Question with RAG
 */
async function example2_QuestionWithRAG() {
    const client = new OfflineAIClient();
    
    try {
        const answer = await client.askWithRAG("Explain deep learning", 5);
        console.log("Answer (with RAG):", answer);
    } catch (error) {
        console.error("Error:", error.message);
    }
}

/**
 * Example 3: Full query with all options
 */
async function example3_FullQuery() {
    const client = new OfflineAIClient();
    
    try {
        const response = await client.query({
            question: "What are neural networks?",
            enableRag: true,
            maxTokens: 300,
            temperature: 0.5,
            topK: 5,
            minRelevanceScore: 0.6
        });
        
        console.log("Answer:", response.answer);
        console.log("Model:", response.model);
        console.log("Used RAG:", response.usedRag);
        console.log("Documents:", response.documentsRetrieved);
        console.log("Time:", response.responseTimeMs, "ms");
        console.log("Tokens:", response.totalTokens);
        console.log("Speed:", response.tokensPerSecond, "tokens/sec");
    } catch (error) {
        console.error("Error:", error.message);
        if (error instanceof OfflineAIError) {
            console.error("Status:", error.statusCode);
            console.error("Details:", error.details);
            console.error("Suggestions:", error.suggestions);
        }
    }
}

/**
 * Example 4: Error handling
 */
async function example4_ErrorHandling() {
    const client = new OfflineAIClient();
    
    try {
        // This will fail - empty question
        await client.query({ question: "" });
    } catch (error) {
        if (error instanceof OfflineAIError) {
            console.error(`Error ${error.statusCode}: ${error.message}`);
            console.error("Details:", error.details);
            console.error("Suggestions:", error.suggestions.join(", "));
        } else {
            console.error("Unexpected error:", error);
        }
    }
}

/**
 * Example 5: Health check
 */
async function example5_HealthCheck() {
    const client = new OfflineAIClient();
    
    try {
        const health = await client.health();
        console.log("API Status:", health.status);
        console.log("Version:", health.version);
        
        const models = await client.getModels();
        console.log("Available models:", models);
    } catch (error) {
        console.error("API is not available:", error.message);
    }
}

/**
 * Example 6: Validate before querying
 */
async function example6_ValidateFirst() {
    const client = new OfflineAIClient();
    
    const request = {
        question: "What is AI?",
        maxTokens: 100,
        temperature: 0.5
    };
    
    try {
        // Validate first
        const validation = await client.validate(request);
        console.log("Request is valid!");
        console.log("Estimated time:", validation.estimatedTimeSeconds, "seconds");
        
        // Now execute
        const response = await client.query(request);
        console.log("Answer:", response.answer);
    } catch (error) {
        console.error("Validation failed:", error.message);
    }
}

/**
 * Example 7: React integration
 */
function ReactExample() {
    // This is a React component example
    const [question, setQuestion] = React.useState('');
    const [answer, setAnswer] = React.useState('');
    const [loading, setLoading] = React.useState(false);
    const [error, setError] = React.useState('');
    
    const client = React.useMemo(() => new OfflineAIClient(), []);
    
    const handleSubmit = async (e) => {
        e.preventDefault();
        setLoading(true);
        setError('');
        setAnswer('');
        
        try {
            const result = await client.query({
                question,
                enableRag: true,
                maxTokens: 512
            });
            setAnswer(result.answer);
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };
    
    return (
        <div>
            <form onSubmit={handleSubmit}>
                <input
                    value={question}
                    onChange={(e) => setQuestion(e.target.value)}
                    placeholder="Ask a question..."
                    disabled={loading}
                />
                <button type="submit" disabled={loading}>
                    {loading ? 'Thinking...' : 'Ask'}
                </button>
            </form>
            
            {error && <div className="error">{error}</div>}
            {answer && <div className="answer">{answer}</div>}
        </div>
    );
}
