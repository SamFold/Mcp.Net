# Agent Management UI Implementation Plan

## Overview
This plan outlines the implementation of a comprehensive Agent Management UI for the Mcp.Net WebUI frontend. The UI will allow users to create, view, edit, and manage AI agents with different configurations, models, and tool access before using them in chat sessions.

## Architecture Overview

### Design Principles
1. **Progressive Disclosure**: Simple agent selection for basic users, advanced configuration for power users
2. **Real-time Updates**: Live synchronization of agent changes across all connected clients
3. **Intuitive Organization**: Clear categorization and search capabilities
4. **Protection of Defaults**: System default agents clearly marked and protected from modification
5. **Seamless Integration**: Natural flow from agent management to chat sessions

### Component Architecture
```
agents/
├── pages/
│   ├── AgentListPage.tsx         # Main agent listing and management
│   ├── AgentDetailPage.tsx       # View/edit specific agent
│   └── AgentCreatePage.tsx       # Create new agent wizard
├── components/
│   ├── AgentCard.tsx             # Agent preview card for grid/list view
│   ├── AgentForm.tsx             # Reusable form for create/edit
│   ├── AgentCategoryFilter.tsx   # Filter agents by category
│   ├── AgentSearchBar.tsx        # Search agents by name/description
│   ├── AgentToolSelector.tsx     # Tool selection component
│   └── AgentParameterEditor.tsx  # Advanced parameter configuration
├── hooks/
│   ├── useAgents.ts              # React Query hooks for agent operations
│   ├── useAgentRealtime.ts       # SignalR subscription for updates
│   └── useAgentValidation.ts     # Form validation logic
└── stores/
    └── agentStore.ts             # Zustand store for agent state
```

## Phase 1: Core Agent Management (MVP)

### 1.1 Navigation & Routing Setup ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** `src/router/index.tsx`, updated `src/App.tsx`, `src/components/layout/AppLayout.tsx`, `src/components/layout/SessionSidebar.tsx`, `src/components/chat/ChatContainer.tsx`

**Implementation Details:**
- Created `src/router/index.tsx` with React Router v6 configuration
- Set up nested routing with AppLayout as the root layout component
- Added routes for:
  - `/` - Home/Chat view (shows ChatContainer)
  - `/chat` - Chat view
  - `/chat/:sessionId` - Specific chat session
  - `/agents` - Agent list page (placeholder)
  - `/agents/new` - Agent creation page (placeholder)
  - `/agents/:agentId` - Agent detail page (placeholder)
- Updated AppLayout to include navigation links in header
- Modified SessionSidebar to navigate to `/chat/:sessionId` URLs when sessions are clicked
- Updated ChatContainer to handle URL parameters and sync with active session state

**Key Learnings:**
1. **Hybrid Navigation:** Maintained existing Zustand state management while adding URL-based navigation for better user experience
2. **Session URL Structure:** Used `/chat/:sessionId` pattern to make sessions bookmarkable and shareable
3. **Navigation Integration:** Added Chat/Agents navigation in header with active state highlighting
4. **Backward Compatibility:** Preserved existing session management logic while enhancing with routing

**Files Modified:**
```typescript
// Created: src/router/index.tsx
const router = createBrowserRouter([
  {
    path: '/',
    element: <RootLayout />,
    children: [
      { index: true, element: <ChatContainer /> },
      { path: 'chat', element: <ChatContainer /> },
      { path: 'chat/:sessionId', element: <ChatContainer /> },
      { path: 'agents', element: <AgentListPage /> },
      { path: 'agents/new', element: <AgentCreatePage /> },
      { path: 'agents/:agentId', element: <AgentDetailPage /> },
    ]
  }
]);

// Updated: App.tsx - Now uses <AppRouter />
// Updated: AppLayout.tsx - Added navigation links and useNavigate
// Updated: SessionSidebar.tsx - Navigate to chat URLs
// Updated: ChatContainer.tsx - Handle URL params with useParams
```

### 1.2 Agent Store Implementation ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** `src/state/agent.ts`, `src/models/agent.ts`, updated `src/lib/api.ts`, updated `src/lib/signalr.ts`

**Implementation Details:**
- Created comprehensive TypeScript models in `src/models/agent.ts`
- Added agent API endpoints to `src/lib/api.ts`
- Enhanced SignalR service with agent event handlers
- Implemented Zustand store with filtering, real-time updates, and error handling

**Enhanced Agent Store Interface:**
```typescript
interface AgentState {
  // State
  agents: AgentSummary[];
  selectedAgent: AgentDetails | null;
  loading: boolean;
  error: string | null;
  filters: AgentFilters;
  
  // Computed
  filteredAgents: AgentSummary[]; // Real-time filtered results
  
  // CRUD Actions
  loadAgents: () => Promise<void>;
  selectAgent: (agentId: string) => Promise<void>;
  createAgent: (agent: CreateAgentDto) => Promise<string>;
  updateAgent: (agentId: string, updates: UpdateAgentDto) => Promise<void>;
  deleteAgent: (agentId: string) => Promise<void>;
  cloneAgent: (agentId: string, newName?: string) => Promise<string>;
  
  // Filter Actions
  setCategory: (category: string | null) => void;
  setSearchTerm: (term: string) => void;
  setProvider: (provider: string | null) => void;
  toggleSystemDefaults: () => void;
  clearFilters: () => void;
  
  // Real-time updates
  handleAgentChanged: (changeType: string, agent: AgentSummary) => void;
  initialize: () => void; // Set up SignalR subscriptions
  cleanup: () => void;    // Clean up subscriptions
}
```

**Key Features Implemented:**
1. **Complete CRUD Operations:** All agent operations with proper error handling
2. **Real-time Filtering:** Client-side filtering with multiple criteria
3. **SignalR Integration:** Automatic updates when agents change
4. **Optimistic Updates:** Immediate UI feedback with server sync
5. **Error Management:** Comprehensive error handling and user feedback
6. **TypeScript Models:** Full type safety with backend DTO matching

**API Integration:**
```typescript
// Added to src/lib/api.ts
export const api = {
  agents: {
    list: async (category?: string): Promise<AgentSummary[]>
    get: async (agentId: string): Promise<AgentDetails>
    create: async (agent: CreateAgentDto): Promise<AgentDetails>
    update: async (agentId: string, updates: UpdateAgentDto): Promise<void>
    delete: async (agentId: string): Promise<void>
    clone: async (agentId: string, newName?: string): Promise<AgentDetails>
  },
  toolCategories: {
    list: async (): Promise<string[]>
    getTools: async (category: string): Promise<Tool[]>
  }
}
```

**SignalR Events Added:**
```typescript
// Added to src/lib/signalr.ts
signalRService.onAgentChanged((data: { changeType: string, agent: AgentSummary }) => {
  // Handle 'created', 'updated', 'deleted' events
});

signalRService.onSessionAgentChanged((data: { sessionId: string, agentId: string, agentName: string }) => {
  // Handle session agent switches
});
```

**Key Learnings:**
1. **Computed Properties:** Using getter functions in Zustand for real-time filtered results
2. **Filter Architecture:** Separate AgentFilters interface for clean filter management
3. **Real-time Sync:** Dual update strategy (optimistic + SignalR confirmation)
4. **Error Boundaries:** Comprehensive error handling at API and store levels
5. **Type Safety:** Full TypeScript coverage matching backend DTOs exactly

**Architecture Decisions Made:**
- **Store Pattern:** Used Zustand instead of React Query for agent state to maintain consistency with existing chat store
- **Filter Design:** Client-side filtering for immediate responsiveness (agents list is typically small)
- **Error Handling:** Store-level error management with UI feedback capabilities
- **Real-time Strategy:** Combined optimistic updates with SignalR confirmation for best UX

**Additional Work Completed:**
- **TypeScript Error Resolution:** Fixed all existing compilation errors in chat system
  - Fixed optional timestamp parameter in `formatTimestamp` function (`src/components/chat/ChatMessage.tsx`)
  - Added `isJoined?: boolean` property to `ChatSession` interface (`src/models/chat.ts`) 
  - Fixed session creation in `addMessage` to include required metadata property (`src/state/chat.ts`)
  - Removed unused `previousSessionId` variable in chat store
- **Build Verification:** Confirmed all components compile successfully with TypeScript
- **Router Integration:** Updated router imports to use actual components instead of placeholders

**Next Steps Required:**
1. **Backend Integration:** Test agent API endpoints with real backend
2. **Agent Creation Wizard:** Implement Phase 2.1 multi-step agent creation form
3. **Agent Detail Enhancement:** Complete agent detail page with full editing capabilities
4. **Chat Integration:** Connect agent selection to chat session creation

### 1.3 Agent List Page ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** `src/pages/AgentListPage.tsx`, `src/components/agents/AgentCard.tsx`, `src/components/agents/AgentCategoryFilter.tsx`, `src/components/agents/AgentSearchBar.tsx`

**Implementation Details:**
- Created comprehensive agent list page with responsive design
- Implemented both grid and list view modes with toggle
- Added category filtering using pill-style buttons
- Implemented debounced search functionality (300ms delay)
- System default agents clearly marked with amber badges
- Created reusable AgentCard component supporting both view modes
- Added empty state handling for no agents found
- Integrated with agent store for real-time filtering

**Features Implemented:**
- Grid/List view toggle with icons
- Category filtering (General, Math, Research, Development, Creative, etc.)
- Search by name/description with debounced input
- System default agents clearly marked with badge
- Quick actions: View, Edit, Clone, Delete (with conditional rendering)
- Loading and error states
- Empty state with helpful messaging
- Back navigation to chat

**Key Learnings from Implementation:**
1. **Component Architecture:** Created modular, reusable components that can be easily extended
2. **State Integration:** Seamless integration with Zustand store using computed properties for filtering
3. **Performance:** Debounced search input prevents excessive filtering operations
4. **User Experience:** Empty states and loading indicators provide clear feedback
5. **Responsive Design:** Grid/list toggle works well across different screen sizes
6. **Type Safety:** Full TypeScript integration with proper interface definitions

**Technical Decisions Made:**
- **AgentCard Flexibility:** Single component handles both grid and list layouts using conditional rendering
- **Category Filter:** Pill-style buttons provide clear visual feedback for active filters
- **Search UX:** Debounced input with clear button for better user experience
- **Navigation:** React Router integration maintains URL state for bookmarkable agent views
- **Error Handling:** Comprehensive error states with retry functionality

**Files Created:**
```typescript
// Pages
src/pages/AgentListPage.tsx     // Main list page with filtering and view toggle
src/pages/AgentCreatePage.tsx   // Placeholder for creation wizard
src/pages/AgentDetailPage.tsx   // Agent details with edit mode support

// Components  
src/components/agents/AgentCard.tsx            // Flexible card component
src/components/agents/AgentCategoryFilter.tsx // Category pill filter
src/components/agents/AgentSearchBar.tsx      // Debounced search input
```

**UI Layout:**
```
┌─────────────────────────────────────────────────┐
│  [← Back] Agent Management        [+ New Agent]  │
├─────────────────────────────────────────────────┤
│  🔍 Search agents...              [Grid] [List]  │
│                                                 │
│  Categories: [All] [General] [Math] [Research]  │
│             [Development] [Creative] [Custom]    │
│                                                 │
│  □ Show System Defaults                         │
├─────────────────────────────────────────────────┤
│  ┌─────────────┐ ┌─────────────┐ ┌────────────┐│
│  │ Agent Card  │ │ Agent Card  │ │ Agent Card ││
│  │             │ │             │ │            ││
│  │ [View][Clone]│ │[View][Clone]│ │[View][Edit]││
│  └─────────────┘ └─────────────┘ └────────────┘│
└─────────────────────────────────────────────────┘
```

### 1.4 Agent Card Component ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** Enhanced `src/components/agents/AgentCard.tsx`

**Implementation Details:**
- Enhanced the existing AgentCard component with comprehensive features
- Added flexible interface supporting different display modes and contexts
- Implemented category-specific icons and provider indicators
- Added support for system default protection (no edit/delete for system agents)
- Created responsive design that works in both grid and list layouts

**Enhanced Interface:**
```typescript
interface AgentCardProps {
  agent: AgentSummary;
  viewMode?: 'grid' | 'list';           // Display mode
  onView: () => void;
  onClone: () => void;
  onEdit?: () => void;
  onDelete?: () => void;
  showActions?: boolean;                 // Hide actions for read-only contexts
  compact?: boolean;                     // Reduce information displayed
}
```

**Key Features Implemented:**
1. **Category-Specific Icons:** Each agent category has a unique icon (Math, Research, Development, Creative, Analytics)
2. **Provider Indicators:** Color-coded dots for different AI providers (OpenAI, Anthropic, Azure)
3. **System Default Protection:** Edit/Delete buttons hidden for system default agents
4. **Flexible Display Modes:** Optimized layouts for both grid and list views
5. **Enhanced Tooltips:** Helpful tooltips on all action buttons
6. **Transition Effects:** Smooth hover animations and color transitions
7. **Compact Mode:** Optional compact display for contexts where space is limited
8. **Conditional Actions:** Actions can be hidden entirely for read-only contexts

**Visual Elements:**
- Agent name and category badge with category-specific colors
- Provider/Model info with visual provider indicator (e.g., "OpenAI 5")
- Description preview with proper text truncation
- System default indicator with amber badge
- Created/Modified dates (hidden in compact mode)
- Action buttons (View, Clone, Edit, Delete) with appropriate permissions

**Grid Layout Features:**
- Prominent agent icon with category-specific design
- Category and system badges at the top
- Full description display with line clamping
- Provider info with visual indicator
- Creation/modification timestamps
- Primary action buttons (View/Clone) with optional Edit/Delete icons

**List Layout Features:**
- Horizontal layout optimized for scanning
- Agent icon, name, and badges in header row
- Inline provider information and timestamps
- Right-aligned action buttons for easy access
- Truncated description for space efficiency

**Key Learnings:**
1. **Icon System:** Created a scalable icon system that can easily accommodate new agent categories
2. **Permission Logic:** Implemented proper permission checks to protect system defaults
3. **Responsive Design:** Single component handles multiple layout contexts effectively
4. **Visual Hierarchy:** Clear information hierarchy helps users quickly identify relevant agents
5. **Accessibility:** Added proper tooltips and ARIA labels for screen readers

### 1.5 Agent Selection in Chat Creation ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** `src/components/agents/AgentSelectionModal.tsx`, updated `src/components/layout/SessionSidebar.tsx`, updated `src/state/chat.ts`, enhanced `src/components/agents/AgentCard.tsx`, updated `src/pages/AgentListPage.tsx`

**Implementation Details:**
- Created comprehensive AgentSelectionModal component for choosing agents when creating chat sessions
- Enhanced SessionSidebar to open agent selection modal instead of directly creating sessions
- Updated chat store to support agent ID in session creation
- Added "Use in Chat" button as primary action in AgentCard components
- Extended ChatSessionMetadata interface to include agent information
- Updated session display to show agent names with visual indicators

**Enhanced Session Creation Modal Implementation:**
```typescript
// Core functionality in AgentSelectionModal.tsx
interface AgentSelectionModalProps {
  isOpen: boolean;
  onClose: () => void;
}

// Key features implemented:
- Agent search and filtering
- Recommended agents section (system defaults)
- Custom agents section
- Optional session title input
- "Browse All Agents" navigation link
- Integration with existing chat creation flow
```

**Features Implemented:**
1. **Agent Selection Modal with Search:** Full-featured modal with real-time filtering and category-based organization
2. **Recommended Agents Section:** Shows system default agents and general-purpose agents prominently
3. **Custom Agents Section:** Displays user-created agents separately
4. **Session Title Configuration:** Optional custom title input for new chat sessions
5. **Agent-Based Session Creation:** Complete integration with backend agent-centric session creation
6. **"Use in Chat" Primary Action:** Prominent button on each AgentCard for immediate chat creation
7. **Agent Information Display:** Sessions now show agent names with visual indicators in sidebar
8. **Cross-Component Integration:** Seamless navigation between agent management and chat creation

**Key Architectural Decisions:**
1. **Modal-First Approach:** Replaced direct session creation with agent selection modal for better UX
2. **Reusable Components:** AgentCard enhanced to support both management and selection contexts
3. **Filtering Integration:** Used existing useFilteredAgents hook for consistent search behavior
4. **Agent-Centric Sessions:** Full support for backend agent-based session creation with fallbacks
5. **Visual Indicators:** Clear differentiation between agent-based and model-based sessions

**Files Created/Modified:**
```typescript
// Created: src/components/agents/AgentSelectionModal.tsx
export function AgentSelectionModal({ isOpen, onClose }: AgentSelectionModalProps) {
  // Comprehensive agent selection with search, filtering, and session creation
}

// Enhanced: src/components/agents/AgentCard.tsx
interface AgentCardProps {
  // Added onUseInChat handler
  onUseInChat?: () => void;
  // Enhanced with primary "Use in Chat" action button
}

// Updated: src/state/chat.ts
createNewSession: async (model?, provider?, systemPrompt?, agentId?) => {
  // Extended to support agent-based session creation
}

// Enhanced: src/components/layout/SessionSidebar.tsx
// Added AgentSelectionModal integration and agent name display

// Updated: src/models/chat.ts
export interface ChatSessionMetadata {
  // Added agent information fields
  agentId?: string;
  agentName?: string;
}
```

**User Experience Flow:**
1. **Session Creation:** User clicks "New Chat" → Agent Selection Modal opens
2. **Agent Selection:** User searches/browses agents → Selects desired agent
3. **Chat Creation:** Modal creates session with selected agent → Navigates to chat
4. **Direct Agent Usage:** From Agent List page → "Use in Chat" button → Immediate session creation
5. **Session Display:** Sidebar shows agent names with 🤖 icons for agent-based sessions

**Integration Points:**
- **Backend Integration:** Full support for agentId parameter in session creation API
- **Routing Integration:** Seamless navigation between agent management and chat
- **State Management:** Consistent state updates across chat and agent stores
- **UI Consistency:** Uniform design patterns across agent cards and modals

**Key Learnings:**
1. **Modal UX Patterns:** Agent selection modal provides better discoverability than hidden options
2. **Component Reusability:** AgentCard flexibility enables use in both management and selection contexts
3. **Integration Complexity:** Proper state management crucial for cross-component agent usage
4. **Visual Feedback:** Agent indicators in session list improve user understanding of session types
5. **Progressive Enhancement:** Agent system enhances existing functionality without breaking compatibility

### 1.6 Welcome Screen Agent Integration ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** Enhanced `src/components/chat/WelcomeScreen.tsx`

**Implementation Details:**
- Replaced model-based welcome screen with agent-centric design
- Added two primary action cards: "Start New Chat" and "Browse All Agents"
- Integrated AgentSelectionModal for chat creation
- Added "Quick Start with Popular Agents" section for immediate access
- Removed direct message input in favor of agent selection first

**Enhanced Welcome Screen Features:**
```typescript
// Key features implemented:
- Agent-focused welcome message
- Two primary action cards with visual icons
- Quick start section with recommended agents
- Direct agent selection for immediate chat creation
- Navigation to full agent management
- Visual agent category icons
- Provider indicators (color-coded dots)
- Loading state management
```

**User Experience Improvements:**
1. **Clear Call-to-Actions:** Two prominent cards guide users to either start a chat or browse agents
2. **Quick Start Options:** Popular agents displayed for one-click chat creation
3. **Visual Hierarchy:** Large icons and clear descriptions make choices obvious
4. **Agent Information:** Each quick-start agent shows category, name, description, and provider
5. **Seamless Integration:** Direct navigation to agent management or immediate chat creation

**Visual Design:**
- **Start New Chat Card:** Indigo theme with chat bubble icon
- **Browse Agents Card:** Purple theme with group icon
- **Quick Start Agents:** Compact cards with hover effects and provider indicators
- **Dark Mode Support:** Full dark mode compatibility with appropriate color schemes
- **Responsive Layout:** Grid layout adapts to screen size (1-3 columns)

**Architecture Benefits:**
- **Consistent Flow:** Welcome screen follows same agent selection pattern as sidebar
- **State Management:** Proper loading and error handling for agent data
- **Performance:** Agents loaded once and cached for quick access
- **Extensibility:** Easy to add more quick actions or agent recommendations

## Phase 2: Agent Creation & Editing

### 2.1 Agent Create/Edit Form ✅ **IMPLEMENTED**
**Status:** Complete  
**Location:** `src/components/agents/AgentCreateWizard.tsx`, `src/components/agents/AgentForm.tsx`, wizard step components in `src/components/agents/wizard/`

**Implementation Details:**
- Created multi-step wizard with 4 steps: Basic Info, Model Config, System Prompt, Tool Selection
- Built reusable form components that can be used for both creation and editing
- Implemented real-time validation and error handling
- Added template system for quick-start with predefined prompts
- Created flexible tool selection interface with category grouping

**Key Components Created:**
1. **AgentCreateWizard.tsx** - Main wizard orchestrator with state management
2. **AgentForm.tsx** - Reusable form component for basic agent properties
3. **AgentBasicInfoStep.tsx** - Step 1: Name, description, and category selection
4. **AgentModelConfigStep.tsx** - Step 2: Provider and model selection with parameters
5. **AgentSystemPromptStep.tsx** - Step 3: System prompt with templates
6. **AgentToolSelectionStep.tsx** - Step 4: Tool selection by category

**Architecture Decisions:**
- **Wizard Pattern:** Used step-by-step wizard for better UX and validation
- **Component Reusability:** Form components designed to work for both create and edit
- **State Management:** Centralized wizard state with partial updates per step
- **Tool Loading:** Dynamic loading from backend with category organization
- **Error Handling:** Per-field validation with user-friendly error messages

**Key Learnings for Editing Phase:**
1. **Form Component Reusability:** The AgentForm component accepts initialData prop, making it perfect for edit mode
2. **Wizard State Pattern:** The updateWizardData function with partial updates works well for editing
3. **Tool Selection Complexity:** Tools need ID mapping (using name as ID) due to API differences
4. **Validation Strategy:** Step-by-step validation ensures data integrity before submission
5. **Template System:** Pre-filled templates greatly improve user experience
6. **API Integration:** Tool categories API provides better organization than flat tool list
7. **Type Safety:** Careful TypeScript typing prevents runtime errors

**Multi-step wizard approach:**

**Step 1: Basic Information**
```
┌─────────────────────────────────────────────────┐
│  Create New Agent                    Step 1 of 4 │
├─────────────────────────────────────────────────┤
│  Basic Information                              │
│                                                 │
│  Name *                                         │
│  [_____________________________]               │
│                                                 │
│  Description *                                  │
│  [_____________________________]               │
│  [_____________________________]               │
│                                                 │
│  Category *                                     │
│  [▼ Select a category          ]               │
│                                                 │
│         [Cancel]              [Next →]          │
└─────────────────────────────────────────────────┘
```

**Step 2: Model Configuration**
```
┌─────────────────────────────────────────────────┐
│  Create New Agent                    Step 2 of 4 │
├─────────────────────────────────────────────────┤
│  Model Configuration                            │
│                                                 │
│  Provider *                                     │
│  ( ) OpenAI  (•) Anthropic  ( ) Other          │
│                                                 │
│  Model *                                        │
│  [▼ claude-3-opus             ]               │
│                                                 │
│  Temperature                   Max Tokens       │
│  [0.7] ────●────              [4000______]     │
│        0         1                              │
│                                                 │
│  Advanced Parameters                            │
│  [+ Add Parameter]                              │
│                                                 │
│      [← Back]                [Next →]          │
└─────────────────────────────────────────────────┘
```

**Step 3: System Prompt**
```
┌─────────────────────────────────────────────────┐
│  Create New Agent                    Step 3 of 4 │
├─────────────────────────────────────────────────┤
│  System Prompt                                  │
│                                                 │
│  Define the agent's behavior and personality:  │
│  ┌─────────────────────────────────────────┐   │
│  │ You are a helpful research assistant     │   │
│  │ specialized in academic literature        │   │
│  │ review and analysis...                    │   │
│  │                                           │   │
│  │                                           │   │
│  └─────────────────────────────────────────┘   │
│                                                 │
│  Templates: [Research] [Coding] [Creative]      │
│                                                 │
│      [← Back]                [Next →]          │
└─────────────────────────────────────────────────┘
```

**Step 4: Tool Selection**
```
┌─────────────────────────────────────────────────┐
│  Create New Agent                    Step 4 of 4 │
├─────────────────────────────────────────────────┤
│  Tool Selection                                 │
│                                                 │
│  Available Tools by Category:                   │
│                                                 │
│  📊 Analytics                                   │
│  ☑ Calculator - Perform calculations           │
│  ☐ Data Analyzer - Analyze datasets            │
│                                                 │
│  🔍 Research                                    │
│  ☑ Web Search - Search the internet            │
│  ☑ Web Scraper - Extract web content           │
│                                                 │
│  🛠️ Utilities                                    │
│  ☐ File Manager - Read/write files             │
│  ☑ Code Executor - Run code snippets           │
│                                                 │
│  Selected: 4 tools                              │
│                                                 │
│      [← Back]           [Create Agent]          │
└─────────────────────────────────────────────────┘
```

### 2.2 Agent Detail Page (Next Phase)
**Recommendations based on creation implementation:**

1. **Edit Mode Architecture:**
   - Reuse wizard step components in a tabbed interface
   - Use same validation logic from creation wizard
   - Implement optimistic updates with rollback on error
   - Add confirmation dialogs for destructive changes

2. **Component Reuse Strategy:**
   - AgentForm can be used directly with initialData prop
   - Wizard steps can become tab panels with minor modifications
   - Tool selection component works as-is for editing
   - Add "isDirty" tracking to show unsaved changes

3. **State Management Pattern:**
   ```typescript
   interface AgentEditState {
     originalAgent: AgentDetails;
     editedAgent: AgentDetails;
     isDirty: boolean;
     editMode: boolean;
     activeTab: 'overview' | 'config' | 'tools' | 'history';
   }
   ```

4. **Implementation Approach:**
   - Load agent details on mount
   - Track changes with deep comparison
   - Show save/cancel buttons only when dirty
   - Implement field-level undo capability
   - Add real-time collaboration warnings

**Read/Edit view with tabs:**
```
┌─────────────────────────────────────────────────┐
│  [← Back] Research Assistant         [Edit] [⋮] │
├─────────────────────────────────────────────────┤
│  [Overview] [Configuration] [Tools] [History]   │
├─────────────────────────────────────────────────┤
│  Overview                                       │
│                                                 │
│  Description:                                   │
│  A specialized agent for academic research...   │
│                                                 │
│  Category: Research                             │
│  Provider: Anthropic                            │
│  Model: claude-3-opus                           │
│                                                 │
│  Created: 2024-01-15 by user@example.com       │
│  Modified: 2024-01-16                          │
│                                                 │
│  [Use in New Chat]  [Clone Agent]  [Delete]    │
└─────────────────────────────────────────────────┘
```

## Phase 3: Real-time Updates & Collaboration

### 3.1 SignalR Integration
```typescript
// hooks/useAgentRealtime.ts
export function useAgentRealtime() {
  const { handleAgentChanged } = useAgentStore();
  const signalR = useSignalRClient();
  
  useEffect(() => {
    // Subscribe to agent changes
    signalR.onAgentChanged((data) => {
      handleAgentChanged(data.changeType, data.agent);
      
      // Show notification
      if (data.changeType === 'created') {
        toast.success(`New agent "${data.agent.name}" created`);
      }
    });
    
    // Subscribe to agent updates
    return () => {
      signalR.off('AgentChanged');
    };
  }, []);
}
```

### 3.2 Optimistic Updates
- Immediate UI updates on user actions
- Rollback on server errors
- Loading states for async operations
- Error boundaries for graceful failures

### 3.3 Conflict Resolution
- Last-write-wins for concurrent edits
- Warning when editing agent modified by another user
- Option to reload latest version

## Phase 4: Advanced Features

### 4.1 Agent Templates
**Quick-start templates for common use cases:**
- Research Assistant (with web search, scraping tools)
- Code Helper (with code execution, file management)
- Creative Writer (with high temperature, creative tools)
- Data Analyst (with calculator, data tools)
- General Assistant (balanced configuration)

### 4.2 Agent Import/Export
```typescript
interface AgentExportFormat {
  version: '1.0';
  agent: {
    name: string;
    description: string;
    category: string;
    provider: string;
    modelName: string;
    systemPrompt: string;
    toolIds: string[];
    parameters: Record<string, any>;
  };
  exportedAt: string;
  exportedBy?: string;
}
```

### 4.3 Agent Analytics Dashboard
```
┌─────────────────────────────────────────────────┐
│  Agent Performance Analytics                    │
├─────────────────────────────────────────────────┤
│  Most Used Agents (Last 30 days)               │
│                                                 │
│  1. Code Helper          ████████████ 45 chats │
│  2. Research Assistant   ████████ 32 chats     │
│  3. Creative Writer      ██████ 28 chats       │
│                                                 │
│  Average Response Time by Agent                 │
│  [Chart showing response times]                 │
│                                                 │
│  Tool Usage by Agent                            │
│  [Heatmap of tool usage patterns]               │
└─────────────────────────────────────────────────┘
```

### 4.4 Agent Sharing & Marketplace
- Share agents with team members
- Public agent marketplace
- Rate and review shared agents
- Fork popular agents

## Implementation Priorities

### Priority 1 (Week 1)
1. ✅ Backend API integration in `api.ts`
2. Agent list page with basic filtering
3. Agent selection in chat creation
4. Real-time update subscriptions

### Priority 2 (Week 2)
1. Agent creation wizard
2. Agent detail/edit page
3. Tool selection component
4. Form validation

### Priority 3 (Week 3)
1. Advanced parameter editor
2. Agent templates
3. Import/export functionality
4. Enhanced search/filter

### Priority 4 (Future)
1. Analytics dashboard
2. Agent marketplace
3. Collaborative editing
4. Version history

## Technical Considerations

### Performance
- Virtualized lists for large agent collections
- Lazy loading of agent details
- Debounced search input
- Optimistic UI updates
- Memoized components

### Accessibility
- Keyboard navigation support
- Screen reader announcements
- Focus management in modals
- High contrast mode support
- ARIA labels and roles

### Security
- Input validation on all forms
- XSS prevention in prompts
- Rate limiting for API calls
- Permission checks for operations
- Audit logging of changes

### Testing Strategy
1. Unit tests for stores and hooks
2. Component tests with React Testing Library
3. E2E tests for critical workflows
4. Visual regression tests
5. Performance benchmarks

## Migration Path

### From Current State to Agent-Based
1. **Phase 1**: Add agent UI without breaking existing chat
2. **Phase 2**: Default all new chats to use agents
3. **Phase 3**: Migrate existing sessions to default agents
4. **Phase 4**: Remove legacy non-agent code paths

### User Communication
- In-app announcement of new agent feature
- Guided tour for first-time users
- Help documentation and tooltips
- Video tutorials for advanced features

## Success Metrics

### Adoption Metrics
- % of new chats using custom agents
- Number of custom agents created per user
- Agent reuse rate
- Time to first custom agent

### Quality Metrics
- Agent creation completion rate
- Error rate in agent operations
- Page load performance
- Real-time sync reliability

### User Satisfaction
- User feedback scores
- Feature request patterns
- Support ticket volume
- Usage retention rates

## Conclusion

This implementation plan provides a comprehensive roadmap for building a powerful yet intuitive Agent Management UI. The phased approach ensures we can deliver value incrementally while building toward a feature-rich agent system that enhances the overall chat experience.

The focus on user experience, real-time collaboration, and progressive disclosure ensures the system remains accessible to new users while providing power features for advanced use cases.