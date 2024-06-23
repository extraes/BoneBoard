using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoneBoard;

internal partial class Hangman
{
    private static string[] hangmanStates =
    {
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
        """,
    };
}
