namespace BoneBoard
{
    internal partial class Hangman
    {
        private static readonly string[] hangmanStates =
        [
            """
                +---+
                |   |
                    |
                    |
                    |
                    |
            =========
            """,
            """
                +---+
                |   |
                O   |
                    |
                    |
                    |
            =========
            """,
            """
                +---+
                |   |
                O   |
                |   |
                    |
                    |
            =========
            """,
            """
                +---+
                |   |
                O   |
               /|   |
                    |
                    |
            =========
            """,
            """
                +---+
                |   |
                O   |
               /|\  |
                    |
                    |
            =========
            """,
            """
                +---+
                |   |
                O   |
               /|\  |
               /    |
                    |
            =========
            """,
            """
                +---+
                |   |
                O   |
               /|\  |
               / \  |
                    |
            =========
            congrats you guys killed him
            """
        ];
    }
}